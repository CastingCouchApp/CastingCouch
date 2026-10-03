//! C#-compatible Spotify state history and recovery data.
use crate::store::SettingsError;
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use serde_json::{json, Map, Value};
use std::{
    collections::HashSet,
    path::{Path, PathBuf},
    sync::Mutex as StdMutex,
    time::{Duration, Instant},
};
use tokio::{
    fs,
    io::{AsyncReadExt, AsyncWriteExt},
    sync::Mutex,
};
use uuid::Uuid;
type Result<T> = std::result::Result<T, SettingsError>;
const MAX_BYTES: usize = 16 * 1024 * 1024;
const HISTORY: &str = "spotify-saved-state-history.json";
const STATES: &str = "spotify-saved-states.json";
const PROFILES: &str = "spotify-history-restore-profiles.json";
fn name_eq(a: &str, b: &str) -> bool {
    a.to_lowercase() == b.to_lowercase()
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct SavedPlaybackState {
    pub context_uri: String,
    pub track: Value,
    pub progress_ms: u32,
    pub volume_percent: u8,
    pub shuffle_enabled: bool,
    pub repeat_mode: String,
    pub was_playing: bool,
    pub saved_at_utc: DateTime<Utc>,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}
impl SavedPlaybackState {
    pub fn validate(&self) -> Result<()> {
        if self.volume_percent > 100
            || !["off", "context", "track"].contains(&self.repeat_mode.as_str())
        {
            return Err(invalid("Ungültiger Spotify-Wiedergabezustand"));
        }
        if self.context_uri.trim().is_empty()
            && self.track["Uri"]
                .as_str()
                .is_none_or(|s| s.trim().is_empty())
        {
            return Err(invalid(
                "Gespeicherter Zustand enthält keinen Titel oder Kontext",
            ));
        }
        Ok(())
    }
}
pub struct SpotifyStateStore {
    root: PathBuf,
    gate: Mutex<()>,
    last_backup: StdMutex<Option<Instant>>,
}
impl SpotifyStateStore {
    pub fn new(root: impl Into<PathBuf>) -> Self {
        Self {
            root: root.into(),
            gate: Mutex::new(()),
            last_backup: StdMutex::new(None),
        }
    }
    pub async fn prepare_folder(&self, backups: bool) -> Result<PathBuf> {
        let path = if backups {
            self.root.join("Backups/SpotifyHistory")
        } else {
            self.root.clone()
        };
        fs::create_dir_all(&path).await?;
        Ok(path)
    }
    async fn states(&self) -> Result<Value> {
        let value = read_or(
            &self.root.join(STATES),
            json!({"FormatVersion":1,"States":{}}),
        )
        .await?;
        if value["FormatVersion"] != 1 || !value["States"].is_object() {
            return Err(invalid("Nicht unterstütztes Spotify-Zustandsformat"));
        }
        for state in value["States"].as_object().unwrap().values() {
            serde_json::from_value::<SavedPlaybackState>(state.clone())?.validate()?;
        }
        Ok(value)
    }
    async fn history(&self) -> Result<Value> {
        normalize_history(
            read_or(&self.root.join(HISTORY), history_default()).await?,
            false,
        )
    }
    async fn custom_profiles(&self) -> Result<Value> {
        let value = read_or(&self.root.join(PROFILES), json!([])).await?;
        Ok(json!(parse_profiles(value)?))
    }
    pub async fn snapshot(&self) -> Result<Value> {
        let _guard = self.gate.lock().await;
        let states = self.states().await?;
        let history = self.history().await?;
        let mut profiles = builtins();
        profiles.extend(self.custom_profiles().await?.as_array().unwrap().clone());
        Ok(
            json!({"states":states["States"],"history":history,"visibleHistory":visible(&history),"profiles":profiles,"backups":self.list_backups().await?}),
        )
    }
    pub async fn get(&self, group: &str) -> Result<SavedPlaybackState> {
        let _guard = self.gate.lock().await;
        let values = self.states().await?;
        let key = find_key(&values["States"], &group_name(group)?)
            .ok_or_else(|| invalid("Für diese Gruppe ist kein Zustand gespeichert"))?;
        let state = serde_json::from_value(values["States"][key].clone())?;
        Ok(state)
    }
    pub async fn save(&self, group: &str, state: SavedPlaybackState) -> Result<()> {
        state.validate()?;
        let group = group_name(group)?;
        let _guard = self.gate.lock().await;
        let old = self.states().await?;
        let mut next = old.clone();
        let mut history = self.history().await?;
        let key = find_key(&next["States"], &group)
            .map(str::to_string)
            .unwrap_or(group.clone());
        next["States"][&key] = serde_json::to_value(&state)?;
        record(
            &mut history,
            "SavedCount",
            1,
            &format!(
                "{group}: '{}' gespeichert",
                state.track["Name"].as_str().unwrap_or("Unbekannter Titel")
            ),
        );
        self.commit_states(&old, &next, &history).await
    }
    /// Remove only the snapshot that was actually restored; concurrent captures survive.
    pub async fn consume(&self, group: &str, state: &SavedPlaybackState) -> Result<()> {
        let _guard = self.gate.lock().await;
        let old = self.states().await?;
        let mut next = old.clone();
        let mut history = self.history().await?;
        if let Some(key) = find_key(&old["States"], &group_name(group)?) {
            if old["States"][key] == serde_json::to_value(state)? {
                next["States"].as_object_mut().unwrap().remove(key);
            }
        }
        record(
            &mut history,
            "RestoredCount",
            1,
            &format!("{group}: Wiedergabe wiederhergestellt"),
        );
        self.commit_states(&old, &next, &history).await
    }
    pub async fn remove(&self, group: &str) -> Result<bool> {
        let _guard = self.gate.lock().await;
        let old = self.states().await?;
        let mut next = old.clone();
        let mut history = self.history().await?;
        let Some(key) = find_key(&old["States"], &group_name(group)?) else {
            return Ok(false);
        };
        next["States"].as_object_mut().unwrap().remove(key);
        record(
            &mut history,
            "DiscardedCount",
            1,
            &format!("{group}: Zustand verworfen"),
        );
        self.commit_states(&old, &next, &history).await?;
        Ok(true)
    }
    pub async fn clear_states(&self) -> Result<usize> {
        let _guard = self.gate.lock().await;
        let old = self.states().await?;
        let mut next = old.clone();
        let mut history = self.history().await?;
        let count = next["States"].as_object().unwrap().len();
        if count == 0 {
            return Ok(0);
        }
        next["States"] = json!({});
        record(
            &mut history,
            "DiscardedCount",
            count,
            &format!("Alle Zustände verworfen ({count})"),
        );
        self.commit_states(&old, &next, &history).await?;
        Ok(count)
    }
    pub async fn cleanup(&self, ttl_minutes: u32, now: DateTime<Utc>) -> Result<usize> {
        let _guard = self.gate.lock().await;
        let old = self.states().await?;
        let mut next = old.clone();
        let mut history = self.history().await?;
        let mut expired = vec![];
        for (name, value) in old["States"].as_object().unwrap() {
            let state: SavedPlaybackState = serde_json::from_value(value.clone())?;
            if now - state.saved_at_utc
                > chrono::Duration::minutes(ttl_minutes.clamp(1, 10080) as i64)
            {
                expired.push(name.clone());
            }
        }
        if expired.is_empty() {
            return Ok(0);
        }
        for name in &expired {
            next["States"].as_object_mut().unwrap().remove(name);
        }
        record(
            &mut history,
            "CleanupCount",
            expired.len(),
            &format!("Bereinigung: {} entfernt", expired.len()),
        );
        self.commit_states(&old, &next, &history).await?;
        Ok(expired.len())
    }
    async fn commit_states(&self, old: &Value, next: &Value, history: &Value) -> Result<()> {
        let bytes = encode(next)?;
        let history_bytes = encode(history)?;
        self.auto_backup().await?;
        atomic_write(&self.root.join(STATES), &bytes).await?;
        if let Err(error) = atomic_write(&self.root.join(HISTORY), &history_bytes).await {
            atomic_write(&self.root.join(STATES), &encode(old)?)
                .await
                .map_err(|rollback| {
                    invalid(&format!("{error}; Rücknahme fehlgeschlagen: {rollback}"))
                })?;
            return Err(error);
        }
        Ok(())
    }
    async fn commit_history(&self, history: &Value) -> Result<()> {
        self.auto_backup().await?;
        atomic_write(&self.root.join(HISTORY), &encode(history)?).await
    }
    pub async fn edit_history(
        &self,
        entries: &[String],
        favorite: Option<bool>,
        note: Option<&str>,
        remove: bool,
    ) -> Result<()> {
        let _guard = self.gate.lock().await;
        let mut history = self.history().await?;
        if note.is_some() && entries.len() != 1 {
            return Err(invalid("Für eine Notiz genau einen Eintrag auswählen"));
        }
        if note.is_some_and(|n| n.len() > 4096) {
            return Err(invalid("Notiz ist zu lang"));
        }
        for entry in entries {
            if !history["Entries"]
                .as_array()
                .unwrap()
                .iter()
                .any(|v| v == entry)
            {
                return Err(invalid("Verlaufseintrag ist nicht mehr vorhanden"));
            }
            if remove {
                history["Entries"]
                    .as_array_mut()
                    .unwrap()
                    .retain(|v| v != entry);
            }
            if let Some(enabled) = favorite {
                history["FavoriteEntries"]
                    .as_array_mut()
                    .unwrap()
                    .retain(|v| v != entry);
                if enabled {
                    history["FavoriteEntries"]
                        .as_array_mut()
                        .unwrap()
                        .push(json!(entry));
                }
            }
            if let Some(note) = note {
                if note.trim().is_empty() {
                    history["Notes"].as_object_mut().unwrap().remove(entry);
                } else {
                    history["Notes"][entry] = json!(note.trim());
                }
            }
        }
        prune(&mut history);
        self.commit_history(&history).await
    }
    pub async fn clear_history(&self) -> Result<()> {
        let _guard = self.gate.lock().await;
        let mut history = self.history().await?;
        history["Entries"] = json!([]);
        history["FavoriteEntries"] = json!([]);
        history["Notes"] = json!({});
        self.commit_history(&history).await
    }
    pub async fn set_filters(&self, filters: Value) -> Result<()> {
        let _guard = self.gate.lock().await;
        let mut history = self.history().await?;
        for key in [
            "SearchText",
            "ActionFilterIndex",
            "SortIndex",
            "FavoritesOnly",
        ] {
            if let Some(value) = filters.get(key) {
                history[key] = value.clone();
            }
        }
        history = normalize_history(history, false)?;
        self.commit_history(&history).await
    }
    pub async fn export_history(&self, selected: Option<&[String]>, csv: bool) -> Result<Vec<u8>> {
        let _guard = self.gate.lock().await;
        let mut history = self.history().await?;
        if let Some(selected) = selected {
            history["Entries"]
                .as_array_mut()
                .unwrap()
                .retain(|v| selected.iter().any(|entry| v == entry));
            prune(&mut history);
        }
        if csv {
            let mut text = "\u{feff}Zeit;Aktion;Favorit;Notiz\r\n".to_string();
            for entry in history["Entries"].as_array().unwrap() {
                let entry = entry.as_str().unwrap();
                let (time, action) = entry.split_once(" · ").unwrap_or(("", entry));
                let favorite = if history["FavoriteEntries"]
                    .as_array()
                    .unwrap()
                    .contains(&json!(entry))
                {
                    "Ja"
                } else {
                    "Nein"
                };
                let note = history["Notes"][entry].as_str().unwrap_or("");
                text.push_str(
                    &[time, action, favorite, note]
                        .map(|v| format!("\"{}\"", v.replace('"', "\"\"")))
                        .join(";"),
                );
                text.push_str("\r\n");
            }
            Ok(text.into_bytes())
        } else {
            history["FormatVersion"] = json!(2);
            history["ExportedAtUtc"] = json!(Utc::now());
            encode(&history)
        }
    }
    pub async fn import_history(&self, text: &str) -> Result<()> {
        let imported = normalize_history(parse_text(text)?, true)?;
        let _guard = self.gate.lock().await;
        let mut next = self.history().await?;
        for key in [
            "SavedCount",
            "RestoredCount",
            "DiscardedCount",
            "CleanupCount",
            "Entries",
            "FavoriteEntries",
            "Notes",
        ] {
            next[key] = imported[key].clone();
        }
        for (key, value) in imported.as_object().unwrap() {
            if !next.as_object().unwrap().contains_key(key) {
                next[key] = value.clone();
            }
        }
        self.create_backup_if_exists().await?;
        atomic_write(&self.root.join(HISTORY), &encode(&next)?).await
    }
    fn backup_path(&self, id: &str) -> Result<PathBuf> {
        if !id.starts_with("spotify-saved-state-history-")
            || !id.ends_with(".json")
            || id.len() > 120
            || !id
                .bytes()
                .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'.')
        {
            return Err(invalid("Ungültige Sicherungs-ID"));
        }
        Ok(self.root.join("Backups/SpotifyHistory").join(id))
    }
    async fn list_backups(&self) -> Result<Vec<Value>> {
        let dir = self.root.join("Backups/SpotifyHistory");
        if !dir.exists() {
            return Ok(vec![]);
        }
        let mut rows = vec![];
        let mut files = fs::read_dir(dir).await?;
        while let Some(entry) = files.next_entry().await? {
            let id = entry.file_name().to_string_lossy().to_string();
            if self.backup_path(&id).is_err() {
                continue;
            }
            let metadata = entry.metadata().await?;
            if !metadata.is_file() {
                continue;
            }
            let at: DateTime<Utc> = metadata.modified()?.into();
            rows.push(json!({"id":id,"at":at,"bytes":metadata.len()}));
        }
        rows.sort_by(|a, b| {
            b["at"]
                .as_str()
                .cmp(&a["at"].as_str())
                .then_with(|| b["id"].as_str().cmp(&a["id"].as_str()))
        });
        Ok(rows)
    }
    async fn create_backup_if_exists(&self) -> Result<Option<String>> {
        if self.root.join(HISTORY).exists() {
            Ok(Some(self.create_backup().await?))
        } else {
            Ok(None)
        }
    }
    async fn create_backup(&self) -> Result<String> {
        let bytes = read_bytes(&self.root.join(HISTORY)).await?;
        normalize_history(serde_json::from_slice(&bytes)?, false)?;
        let id = format!(
            "spotify-saved-state-history-{}-{}.json",
            Utc::now().format("%Y%m%d-%H%M%S"),
            Uuid::new_v4().simple()
        );
        atomic_write(&self.backup_path(&id)?, &bytes).await?;
        *self.last_backup.lock().unwrap() = Some(Instant::now());
        for row in self.list_backups().await?.iter().skip(10) {
            fs::remove_file(self.backup_path(row["id"].as_str().unwrap())?).await?;
        }
        Ok(id)
    }
    async fn auto_backup(&self) -> Result<()> {
        let due = self
            .last_backup
            .lock()
            .unwrap()
            .is_none_or(|last| last.elapsed() >= Duration::from_secs(1800));
        if due {
            self.create_backup_if_exists().await?;
        }
        Ok(())
    }
    pub async fn backup(&self) -> Result<String> {
        let _guard = self.gate.lock().await;
        self.create_backup().await
    }
    pub async fn delete_backup(&self, id: &str) -> Result<()> {
        let _guard = self.gate.lock().await;
        fs::remove_file(self.backup_path(id)?).await?;
        Ok(())
    }
    pub async fn preview_backup(&self, id: &str) -> Result<Value> {
        let _guard = self.gate.lock().await;
        let original = self.history().await?;
        let backup = normalize_history(
            serde_json::from_slice(&read_bytes(&self.backup_path(id)?).await?)?,
            false,
        )?;
        let a: HashSet<String> = strings(&original["Entries"]).into_iter().collect();
        let b: HashSet<String> = strings(&backup["Entries"]).into_iter().collect();
        let mut added: Vec<_> = b.difference(&a).cloned().collect();
        added.sort();
        let mut removed: Vec<_> = a.difference(&b).cloned().collect();
        removed.sort();
        Ok(
            json!({"backup":backup,"original":original,"added":added,"removed":removed,"unchanged":a.intersection(&b).count()}),
        )
    }
    pub async fn restore_history(&self, id: &str, options: Value, original: &Value) -> Result<()> {
        let _guard = self.gate.lock().await;
        let mut current = self.history().await?;
        if &current != original {
            return Err(invalid(
                "Verlauf wurde inzwischen geändert. Vorschau neu laden.",
            ));
        }
        if !["Entries", "Favorites", "Notes", "Counters", "Filters"]
            .iter()
            .any(|key| options[key] == true)
        {
            return Err(invalid(
                "Mindestens einen Bereich zur Wiederherstellung auswählen",
            ));
        }
        let backup = normalize_history(
            serde_json::from_slice(&read_bytes(&self.backup_path(id)?).await?)?,
            false,
        )?;
        if options["Entries"] == true {
            let mut entries = strings(&backup["Entries"]);
            if options["MergeEntries"] == true {
                entries.extend(strings(&current["Entries"]));
            }
            let mut seen = HashSet::new();
            entries.retain(|entry| seen.insert(entry.clone()));
            entries.truncate(100);
            current["Entries"] = json!(entries);
        }
        for (flag, keys) in [
            ("Favorites", vec!["FavoriteEntries"]),
            ("Notes", vec!["Notes"]),
            (
                "Counters",
                vec![
                    "SavedCount",
                    "RestoredCount",
                    "DiscardedCount",
                    "CleanupCount",
                ],
            ),
            (
                "Filters",
                vec![
                    "SearchText",
                    "ActionFilterIndex",
                    "SortIndex",
                    "FavoritesOnly",
                ],
            ),
        ] {
            if options[flag] == true {
                for key in keys {
                    current[key] = backup[key].clone();
                }
            }
        }
        prune(&mut current);
        self.create_backup_if_exists().await?;
        atomic_write(&self.root.join(HISTORY), &encode(&current)?).await
    }
    pub async fn save_profile(&self, profile: Value) -> Result<()> {
        let _guard = self.gate.lock().await;
        let profile = profile_value(profile)?;
        let name = profile["Name"].as_str().unwrap();
        if builtins()
            .iter()
            .any(|p| name_eq(p["Name"].as_str().unwrap(), name))
        {
            return Err(invalid("Integrierte Profile sind schreibgeschützt"));
        }
        let mut custom = self.custom_profiles().await?;
        let rows = custom.as_array_mut().unwrap();
        if let Some(existing) = rows
            .iter_mut()
            .find(|p| name_eq(p["Name"].as_str().unwrap(), name))
        {
            for (k, v) in profile.as_object().unwrap() {
                existing[k] = v.clone();
            }
        } else {
            rows.push(profile);
        }
        atomic_write(&self.root.join(PROFILES), &encode(&custom)?).await
    }
    pub async fn delete_profile(&self, name: &str) -> Result<()> {
        let _guard = self.gate.lock().await;
        if builtins()
            .iter()
            .any(|p| name_eq(p["Name"].as_str().unwrap(), name))
        {
            return Err(invalid("Integrierte Profile können nicht gelöscht werden"));
        }
        let mut custom = self.custom_profiles().await?;
        custom
            .as_array_mut()
            .unwrap()
            .retain(|p| !name_eq(p["Name"].as_str().unwrap(), name));
        atomic_write(&self.root.join(PROFILES), &encode(&custom)?).await
    }
    pub async fn export_profiles(&self) -> Result<Value> {
        let _guard = self.gate.lock().await;
        Ok(
            json!({"Format":"CreatorControlSuite.SpotifyHistoryRestoreProfiles","Version":1,"ExportedAt":Utc::now(),"Profiles":self.custom_profiles().await?}),
        )
    }
    pub async fn preview_profiles_import(&self, text: &str) -> Result<Value> {
        let value = parse_text(text)?;
        if value.is_object() {
            if value.get("Format").is_some_and(|f| {
                f.as_str().is_none_or(|f| {
                    !f.is_empty() && f != "CreatorControlSuite.SpotifyHistoryRestoreProfiles"
                })
            }) || value["Version"].as_u64().is_some_and(|v| v > 1)
            {
                return Err(invalid("Nicht unterstütztes Profilformat"));
            }
        }
        let imported = parse_profiles(if value.is_array() {
            value
        } else {
            value["Profiles"].clone()
        })?;
        if imported.is_empty() {
            return Err(invalid("Datei enthält keine verwendbaren Profile"));
        }
        let _guard = self.gate.lock().await;
        let original = self.custom_profiles().await?;
        let rows=imported.into_iter().map(|profile|{
            let existing=original.as_array().unwrap().iter().find(|p| name_eq(p["Name"].as_str().unwrap(), profile["Name"].as_str().unwrap()));
            json!({"status":match existing{None=>"new",Some(p) if p==&profile=>"unchanged",_=>"changed"},"profile":profile})
        }).collect::<Vec<_>>();
        Ok(json!({"original":original,"profiles":rows}))
    }
    pub async fn import_profiles(
        &self,
        proposals: &Value,
        actions: &[String],
        original: &Value,
    ) -> Result<()> {
        let rows = proposals
            .as_array()
            .ok_or_else(|| invalid("Ungültige Importauswahl"))?;
        if rows.len() != actions.len() {
            return Err(invalid("Importauswahl ist unvollständig"));
        }
        let _guard = self.gate.lock().await;
        let mut custom = self.custom_profiles().await?;
        if &custom != original {
            return Err(invalid(
                "Profile wurden inzwischen geändert. Vorschau neu laden.",
            ));
        }
        for (row, action) in rows.iter().zip(actions) {
            if action == "skip" {
                continue;
            }
            if !["overwrite", "copy"].contains(&action.as_str()) {
                return Err(invalid("Ungültige Profil-Importaktion"));
            }
            let mut profile = profile_value(row["profile"].clone())?;
            let name = profile["Name"].as_str().unwrap().to_string();
            let all_names = builtins()
                .into_iter()
                .chain(custom.as_array().unwrap().clone())
                .collect::<Vec<_>>();
            if action == "copy"
                || builtins()
                    .iter()
                    .any(|p| name_eq(p["Name"].as_str().unwrap(), &name))
            {
                let mut index = 2;
                let mut candidate = name.clone();
                while all_names
                    .iter()
                    .any(|p| name_eq(p["Name"].as_str().unwrap(), &candidate))
                {
                    candidate = format!("{name} {index}");
                    index += 1;
                }
                profile["Name"] = json!(candidate);
                custom.as_array_mut().unwrap().push(profile);
            } else if let Some(existing) = custom
                .as_array_mut()
                .unwrap()
                .iter_mut()
                .find(|p| name_eq(p["Name"].as_str().unwrap(), &name))
            {
                *existing = profile;
            } else {
                custom.as_array_mut().unwrap().push(profile);
            }
        }
        atomic_write(&self.root.join(PROFILES), &encode(&custom)?).await
    }
}
fn invalid(message: &str) -> SettingsError {
    SettingsError::Validation(message.into())
}
fn group_name(group: &str) -> Result<String> {
    let group = group.trim();
    let group = if group.is_empty() { "Standard" } else { group };
    if group.chars().count() > 160 {
        return Err(invalid("Gruppenname ist zu lang"));
    }
    Ok(group.into())
}
fn find_key<'a>(value: &'a Value, name: &str) -> Option<&'a str> {
    value
        .as_object()?
        .keys()
        .find(|key| name_eq(key, name))
        .map(String::as_str)
}
fn strings(v: &Value) -> Vec<String> {
    v.as_array()
        .map(|rows| {
            rows.iter()
                .filter_map(|v| v.as_str().map(str::to_string))
                .collect()
        })
        .unwrap_or_default()
}
fn history_default() -> Value {
    json!({"FormatVersion":1,"SavedCount":0,"RestoredCount":0,"DiscardedCount":0,"CleanupCount":0,"Entries":[],"FavoriteEntries":[],"Notes":{},"SearchText":"","ActionFilterIndex":0,"SortIndex":0,"FavoritesOnly":false})
}
fn normalize_history(mut v: Value, import: bool) -> Result<Value> {
    if v["FormatVersion"] != 1 && !(import && v["FormatVersion"] == 2) {
        return Err(invalid("Nicht unterstütztes Spotify-Verlaufsformat"));
    }
    if v["Entries"]
        .as_array()
        .is_none_or(|rows| rows.iter().any(|r| !r.is_string()))
    {
        return Err(invalid("Ungültige Verlaufseinträge"));
    }
    for (key, default) in history_default().as_object().unwrap() {
        if v.get(key).is_none_or(Value::is_null) {
            v[key] = default.clone();
        }
    }
    v["FormatVersion"] = json!(1);
    for key in [
        "SavedCount",
        "RestoredCount",
        "DiscardedCount",
        "CleanupCount",
    ] {
        let number = v[key]
            .as_i64()
            .ok_or_else(|| invalid("Ungültiger Verlaufszähler"))?;
        v[key] = json!(number.max(0));
    }
    for (key, max) in [("ActionFilterIndex", 4), ("SortIndex", 3)] {
        v[key] = json!(v[key]
            .as_i64()
            .ok_or_else(|| invalid("Ungültiger Verlaufsfilter"))?
            .clamp(0, max));
    }
    if !v["SearchText"].is_string()
        || !v["FavoritesOnly"].is_boolean()
        || !v["FavoriteEntries"].is_array()
        || !v["Notes"].is_object()
    {
        return Err(invalid("Ungültige Verlaufsmetadaten"));
    }
    v["Entries"]
        .as_array_mut()
        .unwrap()
        .retain(|entry| !entry.as_str().unwrap().trim().is_empty());
    v["Entries"].as_array_mut().unwrap().truncate(100);
    prune(&mut v);
    Ok(v)
}
fn prune(v: &mut Value) {
    let entries: HashSet<_> = strings(&v["Entries"]).into_iter().collect();
    v["FavoriteEntries"]
        .as_array_mut()
        .unwrap()
        .retain(|entry| entry.as_str().is_some_and(|s| entries.contains(s)));
    v["Notes"].as_object_mut().unwrap().retain(|key, note| {
        entries.contains(key) && note.as_str().is_some_and(|n| !n.trim().is_empty())
    });
}
fn record(v: &mut Value, key: &str, count: usize, message: &str) {
    v[key] = json!(v[key].as_i64().unwrap_or(0).saturating_add(count as i64));
    v["Entries"].as_array_mut().unwrap().insert(
        0,
        json!(format!(
            "{} · {message}",
            chrono::Local::now().format("%H:%M:%S")
        )),
    );
    v["Entries"].as_array_mut().unwrap().truncate(100);
    prune(v);
}
fn visible(history: &Value) -> Vec<String> {
    let search = history["SearchText"]
        .as_str()
        .unwrap_or("")
        .trim()
        .to_lowercase();
    let action = history["ActionFilterIndex"].as_i64().unwrap_or(0);
    let favorites = strings(&history["FavoriteEntries"]);
    let mut entries = strings(&history["Entries"])
        .into_iter()
        .filter(|entry| {
            let text = entry.to_lowercase();
            let note = history["Notes"][entry]
                .as_str()
                .unwrap_or("")
                .to_lowercase();
            (search.is_empty() || text.contains(&search) || note.contains(&search))
                && (history["FavoritesOnly"] != true || favorites.contains(entry))
                && match action {
                    1 => text.contains("gespeichert"),
                    2 => text.contains("wiederhergestellt"),
                    3 => text.contains("verworfen"),
                    4 => text.contains("bereinigung") || text.contains("bereinigt"),
                    _ => true,
                }
        })
        .collect::<Vec<_>>();
    let mode = history["SortIndex"].as_i64().unwrap_or(0);
    entries.sort_by(|a, b| {
        let (ta, ma) = a.split_once(" · ").unwrap_or(("", a));
        let (tb, mb) = b.split_once(" · ").unwrap_or(("", b));
        match mode {
            1 => ta.cmp(tb),
            2 => ma.to_lowercase().cmp(&mb.to_lowercase()),
            3 => ma
                .split(':')
                .next()
                .unwrap_or(ma)
                .to_lowercase()
                .cmp(&mb.split(':').next().unwrap_or(mb).to_lowercase()),
            _ => tb.cmp(ta),
        }
    });
    entries
}
fn builtins() -> Vec<Value> {
    vec![
        json!({"Name":"Nur Verlauf zusammenführen","Entries":true,"Favorites":false,"Notes":false,"Counters":false,"Filters":false,"MergeEntries":true,"IsBuiltIn":true}),
        json!({"Name":"Verlauf + Favoriten","Entries":true,"Favorites":true,"Notes":false,"Counters":false,"Filters":false,"MergeEntries":true,"IsBuiltIn":true}),
        json!({"Name":"Alles vollständig ersetzen","Entries":true,"Favorites":true,"Notes":true,"Counters":true,"Filters":true,"MergeEntries":false,"IsBuiltIn":true}),
    ]
}
fn profile_value(mut profile: Value) -> Result<Value> {
    let name = profile["Name"]
        .as_str()
        .ok_or_else(|| invalid("Profilname fehlt"))?
        .trim()
        .to_string();
    if name.is_empty() || name.chars().count() > 160 {
        return Err(invalid("Profilname muss 1–160 Zeichen enthalten"));
    }
    profile["Name"] = json!(name);
    profile["IsBuiltIn"] = json!(false);
    for key in [
        "Entries",
        "Favorites",
        "Notes",
        "Counters",
        "Filters",
        "MergeEntries",
    ] {
        if profile.get(key).is_none() {
            profile[key] = json!(false);
        }
        if !profile[key].is_boolean() {
            return Err(invalid("Ungültige Profiloption"));
        }
    }
    Ok(profile)
}
fn parse_profiles(value: Value) -> Result<Vec<Value>> {
    let rows = value
        .as_array()
        .ok_or_else(|| invalid("Ungültige Profilliste"))?;
    let mut result: Vec<Value> = vec![];
    for row in rows {
        if row["Name"].as_str().is_none_or(|n| n.trim().is_empty()) {
            continue;
        }
        let profile = profile_value(row.clone())?;
        let name = profile["Name"].as_str().unwrap();
        result.retain(|p| !name_eq(p["Name"].as_str().unwrap(), name));
        result.push(profile);
    }
    Ok(result)
}
fn parse_text(text: &str) -> Result<Value> {
    if text.len() > MAX_BYTES {
        return Err(invalid("Datei überschreitet 16 MiB"));
    }
    Ok(serde_json::from_str(text.trim_start_matches('\u{feff}'))?)
}
fn encode(value: &Value) -> Result<Vec<u8>> {
    let bytes = serde_json::to_vec_pretty(value)?;
    if bytes.len() > MAX_BYTES {
        return Err(invalid("Datei überschreitet 16 MiB"));
    }
    Ok(bytes)
}
pub async fn read_bytes(path: &Path) -> Result<Vec<u8>> {
    let file = fs::File::open(path).await?;
    let mut bytes = vec![];
    file.take((MAX_BYTES + 1) as u64)
        .read_to_end(&mut bytes)
        .await?;
    if bytes.len() > MAX_BYTES {
        return Err(invalid("Datei überschreitet 16 MiB"));
    }
    Ok(bytes)
}
async fn read_or(path: &Path, default: Value) -> Result<Value> {
    match read_bytes(path).await {
        Ok(bytes) => Ok(serde_json::from_slice(&bytes)?),
        Err(SettingsError::Io(e)) if e.kind() == std::io::ErrorKind::NotFound => Ok(default),
        Err(e) => Err(e),
    }
}
pub async fn atomic_write(path: &Path, bytes: &[u8]) -> Result<()> {
    if bytes.len() > MAX_BYTES {
        return Err(invalid("Datei überschreitet 16 MiB"));
    }
    let parent = path
        .parent()
        .ok_or_else(|| invalid("Ungültiger Dateipfad"))?;
    fs::create_dir_all(parent).await?;
    let temp = parent.join(format!(".spotify-{}.tmp", Uuid::new_v4()));
    let result = async {
        let mut file = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&temp)
            .await?;
        file.write_all(bytes).await?;
        file.sync_all().await?;
        drop(file);
        fs::rename(&temp, path).await?;
        Ok::<_, SettingsError>(())
    }
    .await;
    if result.is_err() {
        let _ = fs::remove_file(&temp).await;
    }
    result
}
