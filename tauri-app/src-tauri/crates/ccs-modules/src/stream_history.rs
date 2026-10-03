use ccs_overlay_server::RealtimeHub;
use chrono::{DateTime, Local, Utc};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{
    collections::{HashMap, HashSet},
    fs::{self, OpenOptions},
    io::{BufRead, BufReader, Read, Seek, SeekFrom, Write},
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
};

#[derive(Clone, Serialize, Deserialize)]
struct Active {
    row: Value,
    last_observed: DateTime<Utc>,
    sample_at: Option<String>,
    follower_start: Option<u64>,
    scene: String,
    #[serde(default)]
    seen: HashSet<String>,
    #[serde(default)]
    track: Option<String>,
    #[serde(skip)]
    recovered: bool,
}
#[derive(Serialize, Deserialize)]
struct Pending {
    kind: String,
    month: String,
    record: Value,
}
#[derive(Serialize, Deserialize)]
struct State {
    version: u32,
    active: Option<Active>,
    pending: Vec<Pending>,
    #[serde(skip)]
    error: Option<String>,
    #[serde(skip)]
    blocked: Option<String>,
    #[serde(skip)]
    ids: HashMap<PathBuf, HashSet<String>>,
    #[serde(skip)]
    metrics: Value,
    #[serde(skip)]
    settings: Value,
    #[serde(skip)]
    stream_transition: Option<DateTime<Utc>>,
}
impl Default for State {
    fn default() -> Self {
        Self {
            version: 1,
            active: None,
            pending: vec![],
            error: None,
            blocked: None,
            ids: HashMap::new(),
            metrics: Value::Null,
            settings: Value::Null,
            stream_transition: None,
        }
    }
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StreamHistorySnapshot {
    pub active: Option<Value>,
    pub sessions: Vec<Value>,
    pub events: Vec<Value>,
    pub statistics: Value,
    pub warnings: Vec<String>,
    pub directory: String,
}
pub struct StreamHistoryRuntime {
    root: PathBuf,
    hub: Arc<RealtimeHub>,
    state: Mutex<State>,
    changes: tokio::sync::broadcast::Sender<()>,
}
impl StreamHistoryRuntime {
    pub fn new(root: PathBuf, hub: Arc<RealtimeHub>) -> Self {
        let checkpoint = root.join("StreamHistory/active-session.json");
        let mut state=match fs::read(&checkpoint) {
            Ok(bytes)=>match serde_json::from_slice::<State>(&bytes) {
                Ok(mut state) if state.version==1=>{if let Some(active)=&mut state.active {active.recovered=true;}state},
                _=>State{blocked:Some("Gespeicherte aktive Sitzung ist beschädigt oder hat ein unbekanntes Format; die Datei bleibt erhalten. Sicherung und Reparatur erforderlich.".into()),..Default::default()},
            },
            Err(error) if error.kind()==std::io::ErrorKind::NotFound=>State::default(),
            Err(error) if error.kind()==std::io::ErrorKind::NotADirectory=>State{error:Some(format!("Sitzungsverzeichnis nicht verfügbar: {error}")),..Default::default()},
            Err(error)=>State{blocked:Some(format!("Aktive Sitzung konnte nicht geladen werden: {error}")),..Default::default()},
        };
        if state.active.as_ref().is_some_and(|a| {
            !a.row.is_object()
                || parse_at(&a.row["StartedAt"]).is_none()
                || a.row["SessionId"].as_str().is_none()
                || a.row["ViewerSamples"].as_array().is_none()
                || !valid_session(&a.row)
        }) || state
            .pending
            .iter()
            .any(|pending| match pending.kind.as_str() {
                "session" => {
                    !valid_session(&pending.record)
                        || parse_at(&pending.record["StartedAt"]).is_none()
                        || pending.record["SessionId"]
                            .as_str()
                            .is_none_or(str::is_empty)
                }
                "journal" => {
                    !valid_month(&pending.month)
                        || parse_at(&pending.record["TimestampUtc"]).is_none()
                        || ["EventId", "SessionId", "Type"]
                            .iter()
                            .any(|key| pending.record[*key].as_str().is_none_or(str::is_empty))
                }
                _ => true,
            })
        {
            state = State {
                blocked: Some("Ungültiger Sitzungs-Checkpoint; Datei bleibt erhalten.".into()),
                ..Default::default()
            };
        }
        Self {
            root,
            hub,
            state: Mutex::new(state),
            changes: tokio::sync::broadcast::channel(64).0,
        }
    }
    pub fn observe(
        &self,
        data: &Value,
        metrics: &Value,
        settings: &Value,
        now: DateTime<Utc>,
    ) -> Result<bool, String> {
        self.observe_internal(data, metrics, settings, now, None, false)
    }
    pub fn update_context(&self, metrics: &Value, settings: &Value) -> Result<(), String> {
        let mut state = self.state.lock().unwrap();
        if let Some(error) = &state.blocked {
            return Err(error.clone());
        }
        state.metrics = metrics.clone();
        state.settings = settings.clone();
        Ok(())
    }
    pub fn begin_poll(&self) -> DateTime<Utc> {
        Utc::now()
    }
    pub fn observe_polled_now(
        &self,
        data: &Value,
        metrics: &Value,
        settings: &Value,
        requested_at: DateTime<Utc>,
    ) -> Result<bool, String> {
        self.observe_polled(data, metrics, settings, requested_at, Utc::now())
    }
    pub fn observe_polled(
        &self,
        data: &Value,
        metrics: &Value,
        settings: &Value,
        requested_at: DateTime<Utc>,
        now: DateTime<Utc>,
    ) -> Result<bool, String> {
        self.observe_internal(data, metrics, settings, now, Some(requested_at), false)
    }
    fn observe_internal(
        &self,
        data: &Value,
        metrics: &Value,
        settings: &Value,
        now: DateTime<Utc>,
        requested_at: Option<DateTime<Utc>>,
        stream_event: bool,
    ) -> Result<bool, String> {
        let mut state = self.state.lock().unwrap();
        let stale = requested_at.is_some_and(|requested| {
            state
                .stream_transition
                .is_some_and(|event| event > requested)
        });
        if stream_event {
            state.stream_transition = Some(now);
        }
        let merged = if !stale && (requested_at.is_some() || stream_event) {
            Some(self.hub.live.merge_snapshot(data))
        } else {
            None
        };
        let data = merged.as_ref().unwrap_or(data);
        // History protection must not suppress independent live OBS state.
        if let Some(error) = &state.blocked {
            return Err(error.clone());
        }
        state.metrics = metrics.clone();
        state.settings = settings.clone();
        let previous_active = state.active.as_ref().map(|active| active.row.clone());
        if let Some(active) = &mut state.active {
            refresh_metadata(active, metrics);
        }
        let mut changed = false;
        if !stale && data["stream"]["available"] == true {
            let live = data["stream"]["isLive"] == true;
            let elapsed = data["stream"]["elapsedSeconds"]
                .as_i64()
                .unwrap_or(0)
                .max(0);
            let inferred = now - chrono::Duration::seconds(elapsed.min(31536000));
            if let Some(active) = state.active.as_ref() {
                let previous_start =
                    parse_at(&active.row["StartedAt"]).unwrap_or(active.last_observed);
                if !live
                    || (active.recovered
                        && (previous_start - inferred).num_seconds().unsigned_abs() > 30)
                {
                    let ended = if active.recovered {
                        active.last_observed
                    } else {
                        now
                    };
                    let interrupted = active.recovered;
                    finish(&mut state, ended, interrupted);
                    changed = true;
                }
            }
            if live && state.active.is_none() {
                let id = format!(
                    "{}-{}",
                    inferred.with_timezone(&Local).format("%Y%m%d-%H%M%S"),
                    &uuid::Uuid::new_v4().simple().to_string()[..16]
                );
                let row = json!({"SessionId":id,"StartedAt":inferred.to_rfc3339(),"EndedAt":null,"DurationSeconds":elapsed,"PeakViewers":0,"AverageViewers":0.0,"FollowersGained":0,"ChatMessages":0,"AlertsPlayed":0,"NewSubscriptions":0,"GiftSubscriptions":0,"BitsCheered":0,"IncomingRaids":0,"RaidEnabled":settings["RaidOnStreamEnd"].as_bool().unwrap_or(false),"RaidTarget":settings["SelectedRaidChannel"].as_str().unwrap_or(""),"Title":metrics["title"].as_str().unwrap_or(""),"Category":metrics["category"].as_str().unwrap_or(""),"ViewerSamples":[],"FollowersKnown":false});
                queue_event(
                    &mut state,
                    &id,
                    "session.started",
                    json!({"startedAt":inferred.to_rfc3339(),"title":row["Title"],"category":row["Category"]}),
                    now,
                );
                state.active = Some(Active {
                    row,
                    last_observed: now,
                    sample_at: None,
                    follower_start: None,
                    scene: String::new(),
                    seen: HashSet::new(),
                    track: None,
                    recovered: false,
                });
                changed = true;
            }
            if live {
                if let Some(active) = &mut state.active {
                    active.last_observed = now;
                    active.recovered = false;
                }
            }
        }
        let mut viewer_event = None;
        if let Some(active) = &mut state.active {
            active.scene = data["obs"]["currentScene"]
                .as_str()
                .unwrap_or(&active.scene)
                .to_string();
            let started = parse_at(&active.row["StartedAt"]).unwrap_or(now);
            if !stale {
                active.row["ObservationAvailable"] = json!(data["stream"]["available"] == true);
            }
            active.row["DurationSeconds"] =
                json!((active.last_observed - started).num_seconds().max(0));
            if metrics["connected"] == true {
                refresh_metadata(active, metrics);
                if metrics["viewerCount"]["error"].is_null()
                    && !stale
                    && data["stream"]["available"] == true
                    && data["stream"]["isLive"] == true
                {
                    if let (Some(viewers), Some(at)) = (
                        metrics["viewerCount"]["value"].as_u64(),
                        metrics["viewerCount"]["at"].as_str(),
                    ) {
                        if active.sample_at.as_deref() != Some(at)
                            && parse_at(&json!(at)).is_some_and(|t| t >= started)
                        {
                            active.sample_at = Some(at.into());
                            active.row["ViewerSamples"]
                                .as_array_mut()
                                .unwrap()
                                .push(json!({"Timestamp":at,"ViewerCount":viewers}));
                            let samples = active.row["ViewerSamples"].as_array().unwrap();
                            let sum = samples
                                .iter()
                                .map(|s| s["ViewerCount"].as_f64().unwrap_or(0.0))
                                .sum::<f64>();
                            active.row["AverageViewers"] = json!(sum / samples.len() as f64);
                            active.row["PeakViewers"] =
                                json!(viewers.max(active.row["PeakViewers"].as_u64().unwrap_or(0)));
                            viewer_event = Some((
                                active.row["SessionId"].as_str().unwrap().to_string(),
                                json!({"viewers":viewers,"scene":active.scene,"category":active.row["Category"],"title":active.row["Title"]}),
                                parse_at(&json!(at)).unwrap(),
                            ));
                            changed = true;
                        }
                    }
                }
            }
        }
        if let Some((id, payload, at)) = viewer_event {
            queue_event(&mut state, &id, "twitch.viewer.sample", payload, at);
        }
        changed |= previous_active != state.active.as_ref().map(|active| active.row.clone());
        self.apply_stats(&state);
        let result = self.flush_locked(&mut state);
        if changed || result.is_err() {
            let _ = self.changes.send(());
        }
        result?;
        Ok(changed)
    }
    pub fn record(&self, event: &Value) {
        // Overlay track notifications intentionally carry reduced metadata. The player
        // snapshot owns journal capture, even when music overlay output is disabled.
        if event["type"] == "app.music.track" {
            return;
        }
        self.record_event(event);
    }
    pub fn record_note(
        &self,
        note: &str,
        request_id: &str,
        at: DateTime<Utc>,
    ) -> Result<(), String> {
        let note = note.trim();
        if note.is_empty() || request_id.trim().is_empty() {
            return Err("Notiz und Anfragekennung dürfen nicht leer sein.".into());
        }
        let mut state = self.state.lock().unwrap();
        if let Some(error) = &state.blocked {
            return Err(error.clone());
        }
        let active = state
            .active
            .as_mut()
            .ok_or("Notizen benötigen eine aktive Stream-Sitzung.")?;
        if active.seen.insert(format!("note:{request_id}")) {
            let id = active.row["SessionId"].as_str().unwrap().to_string();
            let payload = json!({"note":note,"scene":active.scene,"viewers":active.row["ViewerSamples"].as_array().and_then(|s|s.last()).and_then(|s|s["ViewerCount"].as_u64()).unwrap_or(0)});
            queue_event(&mut state, &id, "session.note", payload, at);
        }
        let result = self.flush_locked(&mut state);
        self.notify_change();
        result
    }
    pub(crate) fn notify_change(&self) {
        let _ = self.changes.send(());
    }
    /// Serialized with journal appends: analytics never reads a partially written row.
    /// All historical events are returned; the UI's 500-entry limit does not apply.
    pub(crate) fn analysis_journal(&self) -> Result<(Vec<Value>, Vec<String>, bool), String> {
        let state = self.state.lock().unwrap();
        let mut warnings: Vec<_> = state
            .error
            .iter()
            .chain(state.blocked.iter())
            .cloned()
            .collect();
        let directory = self.root.join("CreatorIntelligence");
        let mut paths = vec![];
        fn visit(directory: &Path, paths: &mut Vec<PathBuf>) -> Result<(), String> {
            let entries = match fs::read_dir(directory) {
                Ok(entries) => entries,
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(()),
                Err(e) => return Err(format!("{}: {e}", directory.display())),
            };
            for entry in entries {
                let entry = entry.map_err(|e| e.to_string())?;
                let kind = entry.file_type().map_err(|e| e.to_string())?;
                if kind.is_dir() {
                    visit(&entry.path(), paths)?;
                } else if kind.is_file() && entry.file_name() == "events.jsonl" {
                    paths.push(entry.path());
                }
            }
            Ok(())
        }
        visit(&directory, &mut paths)?;
        paths.sort();
        let mut events = vec![];
        for path in paths {
            // Read errors must fail analytics rather than silently producing false goals.
            let bytes =
                fs::read_to_string(&path).map_err(|e| format!("{}: {e}", path.display()))?;
            let mut damaged = 0;
            for line in bytes
                .trim_start_matches('\u{feff}')
                .lines()
                .filter(|s| !s.trim().is_empty())
            {
                match serde_json::from_str::<Value>(line) {
                    Ok(row) if row.is_object() => events.push(row),
                    _ => damaged += 1,
                }
            }
            if damaged > 0 {
                warnings.push(format!(
                    "{}: {damaged} beschädigte Zeile(n) übersprungen; Original bleibt erhalten.",
                    path.display()
                ));
            }
        }
        Ok((events, warnings, state.active.is_some()))
    }
    pub fn observe_stream_event(&self, active: bool, at: DateTime<Utc>) -> Result<bool, String> {
        let (metrics, settings) = {
            let state = self.state.lock().unwrap();
            (state.metrics.clone(), state.settings.clone())
        };
        let mut data = json!({"obs":{"outputs":{"stream":{"outputActive":active}}},"stream":{"available":true,"isLive":active}});
        if active {
            data["obs"]["outputs"]["stream"]["outputDuration"] = json!(0);
            data["stream"]["elapsedSeconds"] = json!(0);
        }
        self.observe_internal(&data, &metrics, &settings, at, None, true)
    }
    pub fn record_music(
        &self,
        music: &crate::music_player::MusicPlayerSnapshot,
        now: DateTime<Utc>,
    ) {
        if !music.connected || music.title.trim().is_empty() {
            return;
        }
        self.record_event(&json!({"source":"app","type":"session.music.track","at":now.to_rfc3339(),"data":{"provider":music.provider,"title":music.title,"artist":music.artist,"album":music.album,"isPlaying":music.is_playing}}));
    }
    pub fn record_music_now(&self, music: &crate::music_player::MusicPlayerSnapshot) {
        self.record_music(music, Utc::now());
    }
    fn record_event(&self, event: &Value) {
        let mut state = self.state.lock().unwrap();
        if state.blocked.is_some() {
            return;
        }
        let Some(active) = &mut state.active else {
            return;
        };
        let kind = event["type"].as_str().unwrap_or("");
        let source = event["source"].as_str().unwrap_or("");
        if source != "twitch"
            && !matches!(kind, "app.alert" | "app.obs.scene" | "session.music.track")
        {
            return;
        }
        let message = event["data"]["messageId"]
            .as_str()
            .or_else(|| event["data"]["message_id"].as_str())
            .unwrap_or("");
        let event_id = event["data"]["eventSubMessageId"].as_str().unwrap_or("");
        let mut keys = vec![];
        if !event_id.is_empty() {
            keys.push(format!("event:{event_id}"));
        }
        if kind == "channel.chat.message" && !message.is_empty() {
            keys.push(format!("chat:{message}"));
        }
        if keys.iter().any(|key| active.seen.contains(key)) {
            // The live hub receives the envelope before journal capture. Restore
            // authoritative counters when an EventSub delivery is repeated.
            self.apply_stats(&state);
            return;
        }
        active.seen.extend(keys);
        if kind == "session.music.track" {
            let signature = json!([
                event["data"]["provider"],
                event["data"]["artist"],
                event["data"]["title"],
                event["data"]["album"]
            ])
            .to_string();
            if active.track.as_ref() == Some(&signature) {
                return;
            }
            active.track = Some(signature);
        }
        let at = parse_at(&event["at"]).unwrap_or_else(Utc::now);
        let metric = match kind {
            "channel.chat.message" => Some(("ChatMessages", 1)),
            "channel.subscribe" | "channel.subscription.message" => Some(("NewSubscriptions", 1)),
            "channel.subscription.gift" => {
                Some(("GiftSubscriptions", numeric(&event["data"]["total"]).max(1)))
            }
            "channel.cheer" => Some(("BitsCheered", numeric(&event["data"]["bits"]).max(1))),
            "channel.raid" => Some(("IncomingRaids", 1)),
            "app.alert" => Some(("AlertsPlayed", 1)),
            _ => None,
        };
        if let Some((key, amount)) = metric {
            active.row[key] = json!(active.row[key].as_u64().unwrap_or(0).saturating_add(amount));
        }
        let id = active.row["SessionId"].as_str().unwrap().to_string();
        let viewers = active.row["ViewerSamples"]
            .as_array()
            .and_then(|s| s.last())
            .and_then(|s| s["ViewerCount"].as_u64())
            .unwrap_or(0);
        let (journal_kind, payload) = if kind == "channel.chat.message" {
            let user = event["data"]
                .get("userName")
                .or_else(|| event["data"].get("chatter_user_name"))
                .cloned()
                .unwrap_or(Value::Null);
            (
                "twitch.chat.message",
                json!({"user":user,"scene":active.scene,"viewers":viewers}),
            )
        } else if source == "twitch" {
            (
                "twitch.event",
                json!({"type":kind,"summary":event["summary"],"scene":active.scene,"viewers":viewers}),
            )
        } else if kind == "app.obs.scene" {
            if let Some(scene) = event["data"]["scene"].as_str() {
                active.scene = scene.into();
            }
            (
                "obs.scene.changed",
                json!({"scene":active.scene,"viewers":viewers}),
            )
        } else if kind == "session.music.track" {
            (
                "spotify.track.changed",
                json!({"title":event["data"]["title"],"artist":event["data"]["artist"],"album":event["data"]["album"],"isPlaying":event["data"]["isPlaying"],"scene":active.scene,"viewers":viewers}),
            )
        } else {
            (
                "alert.played",
                json!({"type":kind,"summary":event["summary"],"scene":active.scene,"viewers":viewers}),
            )
        };
        queue_event(&mut state, &id, journal_kind, payload, at);
        if kind == "channel.follow" {
            queue_event(
                &mut state,
                &id,
                "twitch.follow",
                json!({"Summary":event["summary"]}),
                at,
            );
        }
        self.apply_stats(&state);
        let _ = self.flush_locked(&mut state);
        let _ = self.changes.send(());
    }
    fn apply_stats(&self, state: &State) {
        let row = state.active.as_ref().map(|a| &a.row).or_else(|| {
            state
                .pending
                .iter()
                .rev()
                .find(|p| p.kind == "session")
                .map(|p| &p.record)
        });
        if let Some(row) = row {
            let mut data = self.hub.live.data.write().unwrap();
            data["stream"]["elapsedSeconds"] = row["DurationSeconds"].clone();
            data["stream"]["startedAt"] = row["StartedAt"].clone();
            data["stream"]["endedAt"] = row["EndedAt"].clone();
            for (field, key) in [
                ("DurationSeconds", "streamTimeSeconds"),
                ("PeakViewers", "peakViewers"),
                ("AverageViewers", "averageViewers"),
                ("FollowersGained", "followersGained"),
                ("ChatMessages", "chatMessages"),
                ("AlertsPlayed", "alertsPlayed"),
                ("NewSubscriptions", "newSubscriptions"),
                ("GiftSubscriptions", "giftSubscriptions"),
                ("BitsCheered", "bitsCheered"),
                ("IncomingRaids", "incomingRaids"),
            ] {
                data["stats"][key] = row[field].clone();
            }
        }
    }
    pub fn retry(&self) -> Result<(), String> {
        let result = self.flush_locked(&mut self.state.lock().unwrap());
        let _ = self.changes.send(());
        result
    }
    pub fn subscribe_changes(&self) -> tokio::sync::broadcast::Receiver<()> {
        self.changes.subscribe()
    }
    pub fn observe_now(
        &self,
        data: &Value,
        metrics: &Value,
        settings: &Value,
    ) -> Result<bool, String> {
        self.observe(data, metrics, settings, Utc::now())
    }
    fn flush_locked(&self, state: &mut State) -> Result<(), String> {
        if let Some(error) = &state.blocked {
            return Err(error.clone());
        }
        let result = (|| {
            // Save the queue first; journal IDs also suppress replay after a crash between writes.
            checkpoint(
                &self.root.join("StreamHistory/active-session.json"),
                &serde_json::to_vec(state).map_err(|e| e.to_string())?,
            )?;
            while let Some(item) = state.pending.first() {
                let path = if item.kind == "session" {
                    self.root.join("StreamHistory/history.jsonl")
                } else if valid_month(&item.month) {
                    self.root
                        .join("CreatorIntelligence")
                        .join(&item.month)
                        .join("events.jsonl")
                } else {
                    return Err("Ungültiges Journalziel im Checkpoint.".into());
                };
                let ids = state
                    .ids
                    .entry(path.clone())
                    .or_insert_with(|| existing_ids(&path));
                let key = item.record[if item.kind == "session" {
                    "SessionId"
                } else {
                    "EventId"
                }]
                .as_str()
                .unwrap_or("")
                .to_string();
                if key.is_empty() {
                    return Err("Persistenter Datensatz besitzt keine Kennung.".into());
                }
                if !ids.contains(&key) {
                    if let Err(error) = append_jsonl(&path, &item.record) {
                        state.ids.remove(&path);
                        return Err(error);
                    }
                    ids.insert(key);
                }
                state.pending.remove(0);
            }
            checkpoint(
                &self.root.join("StreamHistory/active-session.json"),
                &serde_json::to_vec(state).map_err(|e| e.to_string())?,
            )?;
            Ok::<_, String>(())
        })();
        state.error = result
            .as_ref()
            .err()
            .map(|e| format!("Sitzungsverlauf konnte nicht gespeichert werden: {e}"));
        result
    }
    pub fn snapshot(&self, session: Option<&str>) -> Result<StreamHistorySnapshot, String> {
        let state = self.state.lock().unwrap();
        let mut warnings = state
            .error
            .iter()
            .chain(state.blocked.iter())
            .cloned()
            .collect::<Vec<_>>();
        let mut sessions = read_jsonl(
            &self.root.join("StreamHistory/history.jsonl"),
            &mut warnings,
        );
        sessions.retain(|row| valid_session(row));
        sessions.sort_by(|a, b| parse_at(&b["StartedAt"]).cmp(&parse_at(&a["StartedAt"])));
        let statistics = statistics(&sessions);
        let active = state.active.as_ref().map(|a| {
            let mut row = a.row.clone();
            row["Recovered"] = json!(a.recovered);
            row
        });
        let mut events = vec![];
        let journal = self.root.join("CreatorIntelligence");
        match fs::read_dir(&journal) {
            Ok(dirs) => {
                let mut paths = dirs
                    .filter_map(Result::ok)
                    .filter(|e| {
                        e.file_type().is_ok_and(|t| t.is_dir())
                            && valid_month(&e.file_name().to_string_lossy())
                    })
                    .map(|e| e.path().join("events.jsonl"))
                    .collect::<Vec<_>>();
                paths.sort();
                for path in paths {
                    events.extend(read_jsonl(&path, &mut warnings).into_iter().filter(|e| {
                        e["Type"].is_string() && session.is_none_or(|s| e["SessionId"] == s)
                    }));
                }
            }
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => warnings.push(format!("Journal nicht lesbar: {error}")),
        }
        if events.len() > 500 {
            events.drain(..events.len() - 500);
        }
        events.reverse();
        Ok(StreamHistorySnapshot {
            active,
            sessions,
            events,
            statistics,
            warnings,
            directory: self.root.join("StreamHistory").to_string_lossy().into(),
        })
    }
    pub fn export(&self, format: &str, path: &Path) -> Result<(), String> {
        if !matches!(format, "csv" | "html")
            || path.extension().and_then(|s| s.to_str()) != Some(format)
        {
            return Err("Exportziel muss eine CSV- oder HTML-Datei sein.".into());
        }
        let rows = read_jsonl(&self.root.join("StreamHistory/history.jsonl"), &mut vec![]);
        let bytes = if format == "csv" {
            csv(&rows)
        } else {
            let rows = rows
                .into_iter()
                .filter(|row| valid_session(row) && parse_at(&row["StartedAt"]).is_some())
                .collect::<Vec<_>>();
            if rows.is_empty() {
                return Err(
                    "Für einen Stream-Report werden abgeschlossene Streams benötigt.".into(),
                );
            }
            html(&rows, &statistics(&rows))
        };
        checkpoint(path, format!("\u{feff}{bytes}").as_bytes())
    }
    pub fn latest_summary(&self) -> Result<String, String> {
        let rows = read_jsonl(&self.root.join("StreamHistory/history.jsonl"), &mut vec![]);
        let row = rows
            .iter()
            .rev()
            .find(|row| valid_session(row) && parse_at(&row["StartedAt"]).is_some())
            .ok_or("Noch kein abgeschlossener Stream gespeichert.")?;
        let start = parse_at(&row["StartedAt"])
            .map(|t| t.with_timezone(&Local).format("%d.%m.%Y").to_string())
            .unwrap_or_default();
        Ok(format!("Stream-Zusammenfassung vom {start}\nTitel: {}\nKategorie: {}\nDauer: {}\nPeak: {} Zuschauer | Durchschnitt: {:.1}\nNeue Follower: {} | Chatnachrichten: {}",text(&row["Title"]),text(&row["Category"]),duration_hms(numeric(&row["DurationSeconds"])),numeric(&row["PeakViewers"]),row["AverageViewers"].as_f64().unwrap_or(0.0),numeric(&row["FollowersGained"]),numeric(&row["ChatMessages"])))
    }
}
fn finish(state: &mut State, at: DateTime<Utc>, interrupted: bool) {
    if let Some(mut active) = state.active.take() {
        let start = parse_at(&active.row["StartedAt"]).unwrap_or(at);
        active.row["EndedAt"] = json!(at.to_rfc3339());
        active.row["DurationSeconds"] = json!((at - start).num_seconds().max(0));
        active.row["Interrupted"] = json!(interrupted);
        let id = active.row["SessionId"].as_str().unwrap().to_string();
        queue_event(
            state,
            &id,
            "session.ended",
            json!({"endedAt":at.to_rfc3339(),"interrupted":interrupted}),
            at,
        );
        state.pending.push(Pending {
            kind: "session".into(),
            month: String::new(),
            record: active.row,
        });
    }
}
fn refresh_metadata(active: &mut Active, metrics: &Value) {
    if metrics["connected"] != true {
        return;
    }
    if metrics["channelError"].is_null() {
        if let Some(title) = metrics["title"].as_str() {
            active.row["Title"] = json!(title);
        }
        if let Some(category) = metrics["category"].as_str() {
            active.row["Category"] = json!(category);
        }
    }
    if metrics["followers"]["error"].is_null() {
        if let Some(followers) = metrics["followers"]["value"].as_u64() {
            let baseline = *active.follower_start.get_or_insert(followers);
            active.row["FollowersGained"] = json!(followers.saturating_sub(baseline));
            active.row["FollowersKnown"] = json!(true);
        }
    }
}
fn queue_event(state: &mut State, id: &str, kind: &str, payload: Value, at: DateTime<Utc>) {
    state.pending.push(Pending{kind:"journal".into(),month:at.with_timezone(&Local).format("%Y-%m").to_string(),record:json!({"TimestampUtc":at.to_rfc3339(),"SessionId":id,"Type":kind,"Payload":payload,"EventId":uuid::Uuid::new_v4().to_string()})});
}
fn valid_month(month: &str) -> bool {
    month.len() == 7
        && month.as_bytes()[4] == b'-'
        && month
            .chars()
            .enumerate()
            .all(|(i, c)| i == 4 || c.is_ascii_digit())
        && month[5..]
            .parse::<u32>()
            .is_ok_and(|m| (1..=12).contains(&m))
}
fn parse_at(value: &Value) -> Option<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(value.as_str()?)
        .ok()
        .map(|t| t.with_timezone(&Utc))
}
fn numeric(value: &Value) -> u64 {
    value
        .as_u64()
        .or_else(|| value.as_str().and_then(|s| s.parse().ok()))
        .unwrap_or(0)
}
fn text(value: &Value) -> String {
    if value.is_null() {
        String::new()
    } else {
        value
            .as_str()
            .map(str::to_string)
            .unwrap_or_else(|| value.to_string())
    }
}
fn valid_session(row: &Value) -> bool {
    row.is_object()
        && row["StartedAt"]
            .as_str()
            .is_none_or(|_| parse_at(&row["StartedAt"]).is_some())
        && [
            "DurationSeconds",
            "PeakViewers",
            "FollowersGained",
            "ChatMessages",
            "NewSubscriptions",
            "GiftSubscriptions",
            "BitsCheered",
            "IncomingRaids",
            "AlertsPlayed",
        ]
        .iter()
        .all(|key| row[*key].is_null() || row[*key].as_i64().is_some())
        && (row["AverageViewers"].is_null()
            || row["AverageViewers"].as_f64().is_some_and(f64::is_finite))
}
fn read_jsonl(path: &Path, warnings: &mut Vec<String>) -> Vec<Value> {
    let file = match fs::File::open(path) {
        Ok(file) => file,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return vec![],
        Err(error) => {
            warnings.push(format!("{}: {error}", path.display()));
            return vec![];
        }
    };
    let mut result = vec![];
    let mut damaged = 0;
    for line in BufReader::new(file).lines() {
        match line {
            Ok(line) => {
                let line = line.trim_start_matches('\u{feff}').trim();
                if line.is_empty() {
                    continue;
                }
                match serde_json::from_str::<Value>(line) {
                    Ok(value) if value.is_object() => result.push(value),
                    _ => damaged += 1,
                }
            }
            Err(error) => {
                warnings.push(format!("{}: {error}", path.display()));
                break;
            }
        }
    }
    if damaged > 0 {
        warnings.push(format!(
            "{}: {damaged} beschädigte Zeile(n) übersprungen; Original bleibt erhalten.",
            path.display()
        ));
    }
    result
}
fn existing_ids(path: &Path) -> HashSet<String> {
    read_jsonl(path, &mut vec![])
        .iter()
        .filter_map(|v| {
            v["EventId"]
                .as_str()
                .or_else(|| v["SessionId"].as_str())
                .map(str::to_string)
        })
        .collect()
}
fn append_jsonl(path: &Path, value: &Value) -> Result<(), String> {
    let result = (|| {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        let mut file = OpenOptions::new()
            .create(true)
            .read(true)
            .append(true)
            .open(path)?;
        if file.metadata()?.len() > 0 {
            file.seek(SeekFrom::End(-1))?;
            let mut byte = [0];
            file.read_exact(&mut byte)?;
            if byte[0] != b'\n' {
                file.write_all(b"\n")?;
            }
        }
        file.write_all(value.to_string().as_bytes())?;
        file.write_all(b"\n")?;
        file.sync_data()?;
        Ok::<_, std::io::Error>(())
    })();
    result.map_err(|e| format!("{}: {e}", path.display()))
}
pub(crate) fn checkpoint(path: &Path, bytes: &[u8]) -> Result<(), String> {
    let parent = path.parent().ok_or("Ungültiger Dateipfad")?;
    fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    let temp = parent.join(format!(".history-{}.tmp", uuid::Uuid::new_v4()));
    let result = (|| {
        let mut file = OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&temp)?;
        file.write_all(bytes)?;
        file.sync_all()?;
        drop(file);
        fs::rename(&temp, path)?;
        Ok::<_, std::io::Error>(())
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temp);
    }
    result.map_err(|e| format!("{}: {e}", path.display()))
}
fn duration_hms(seconds: u64) -> String {
    format!(
        "{:02}:{:02}:{:02}",
        seconds / 3600 % 24,
        seconds / 60 % 60,
        seconds % 60
    )
}
pub fn format_duration(seconds: u64) -> String {
    if seconds >= 86400 {
        format!(
            "{}d {:02}:{:02}",
            seconds / 86400,
            seconds / 3600 % 24,
            seconds / 60 % 60
        )
    } else {
        format!("{:02}:{:02}", seconds / 3600, seconds / 60 % 60)
    }
}
fn statistics(rows: &[Value]) -> Value {
    let total = rows
        .iter()
        .map(|r| numeric(&r["DurationSeconds"]))
        .sum::<u64>();
    let average =
        |r: &Value| ((r["AverageViewers"].as_f64().unwrap_or(0.0) * 10.0).round_ties_even()) / 10.0;
    let avg = if total > 0 {
        rows.iter()
            .map(|r| average(r) * numeric(&r["DurationSeconds"]) as f64)
            .sum::<f64>()
            / total as f64
    } else if rows.is_empty() {
        0.0
    } else {
        rows.iter().map(average).sum::<f64>() / rows.len() as f64
    };
    let mut categories: Vec<Value> = vec![];
    for row in rows {
        let name = text(&row["Category"]);
        let name = if name.trim().is_empty() {
            "Nicht angegeben".into()
        } else {
            name
        };
        if let Some(item) = categories
            .iter_mut()
            .find(|c| c["name"].as_str().unwrap().eq_ignore_ascii_case(&name))
        {
            item["count"] = json!(numeric(&item["count"]) + 1);
            item["seconds"] = json!(numeric(&item["seconds"]) + numeric(&row["DurationSeconds"]));
            item["viewerSum"] = json!(item["viewerSum"].as_f64().unwrap_or(0.0) + average(row));
        } else {
            categories.push(json!({"name":name,"count":1,"seconds":numeric(&row["DurationSeconds"]),"viewerSum":average(row)}));
        }
    }
    categories.sort_by(|a, b| {
        numeric(&b["count"])
            .cmp(&numeric(&a["count"]))
            .then(numeric(&b["seconds"]).cmp(&numeric(&a["seconds"])))
    });
    for c in &mut categories {
        c["averageViewers"] =
            json!(c["viewerSum"].as_f64().unwrap_or(0.0) / numeric(&c["count"]).max(1) as f64);
    }
    let mut development = rows
        .iter()
        .filter(|r| parse_at(&r["StartedAt"]).is_some())
        .take(20)
        .cloned()
        .collect::<Vec<_>>();
    development.reverse();
    json!({"totalStreams":rows.len(),"totalDuration":format_duration(total),"totalSeconds":total,"averageViewers":avg,"peakViewers":rows.iter().map(|r|numeric(&r["PeakViewers"])).max().unwrap_or(0),"followers":rows.iter().map(|r|numeric(&r["FollowersGained"])).sum::<u64>(),"averageDuration":format_duration(if rows.is_empty(){0}else{total/rows.len() as u64}),"categories":categories,"development":development})
}
fn csv(rows: &[Value]) -> String {
    let keys = [
        "StartedAt",
        "EndedAt",
        "DurationSeconds",
        "PeakViewers",
        "AverageViewers",
        "FollowersGained",
        "ChatMessages",
        "Category",
        "Title",
    ];
    let mut lines = vec![keys.join(";")];
    for row in rows {
        lines.push(
            keys.iter()
                .map(|k| text(&row[*k]).replace(';', ",").replace(['\r', '\n'], " "))
                .collect::<Vec<_>>()
                .join(";"),
        );
    }
    lines.join("\r\n") + "\r\n"
}
fn html(rows: &[Value], stats: &Value) -> String {
    fn h(value: &Value) -> String {
        text(value)
            .replace('&', "&amp;")
            .replace('<', "&lt;")
            .replace('>', "&gt;")
            .replace('"', "&quot;")
            .replace('\'', "&#39;")
    }
    let mut ordered = rows.iter().collect::<Vec<_>>();
    ordered.sort_by(|a, b| parse_at(&b["StartedAt"]).cmp(&parse_at(&a["StartedAt"])));
    let table=ordered.iter().take(50).map(|r|format!("<tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{:.1}</td><td>{}</td><td>{}</td></tr>",parse_at(&r["StartedAt"]).map(|t|t.with_timezone(&Local).format("%d.%m.%Y %H:%M").to_string()).unwrap_or_else(||"-".into()),h(&r["Title"]),h(&r["Category"]),duration_hms(numeric(&r["DurationSeconds"])),numeric(&r["PeakViewers"]),r["AverageViewers"].as_f64().unwrap_or(0.0),numeric(&r["FollowersGained"]),numeric(&r["ChatMessages"]))).collect::<Vec<_>>().join("");
    let recent = ordered.iter().take(5).collect::<Vec<_>>();
    let avg = if recent.is_empty() {
        0.0
    } else {
        recent
            .iter()
            .map(|r| r["AverageViewers"].as_f64().unwrap_or(0.0))
            .sum::<f64>()
            / recent.len() as f64
    };
    let recent_peak = if recent.is_empty() {
        0.0
    } else {
        recent
            .iter()
            .map(|r| numeric(&r["PeakViewers"]) as f64)
            .sum::<f64>()
            / recent.len() as f64
    };
    // The C# report intentionally groups case-sensitively and uses raw means,
    // while the statistics page combines categories and rounds individual means.
    let mut categories: Vec<(String, f64, usize)> = vec![];
    for row in rows {
        let name = text(&row["Category"]);
        if name.trim().is_empty() || name == "-" {
            continue;
        }
        let average = row["AverageViewers"].as_f64().unwrap_or(0.0);
        if let Some(category) = categories.iter_mut().find(|c| c.0 == name) {
            category.1 += average;
            category.2 += 1;
        } else {
            categories.push((name, average, 1));
        }
    }
    categories.sort_by(|a, b| (b.1 / b.2 as f64).total_cmp(&(a.1 / a.2 as f64)));
    let best = categories
        .first()
        .map(|c| h(&json!(c.0)))
        .unwrap_or_else(|| "-".into());
    let hours = stats["totalSeconds"].as_f64().unwrap_or(0.0) / 3600.0;
    let chat = rows
        .iter()
        .map(|r| numeric(&r["ChatMessages"]))
        .sum::<u64>();
    format!("<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>Twitch Stream-Report</title><style>body{{font-family:Segoe UI,Arial;background:#0b1014;color:#eef3f6;margin:32px}}table{{width:100%;border-collapse:collapse}}th,td{{padding:10px;border-bottom:1px solid #2a3740;text-align:left}}</style></head><body><h1>CastingCouch – Twitch Stream-Report</h1><p>Streams: {} · Rekord-Peak: {} · Bestes Ø: {:.1} · Livezeit: {} · Follower: {} · Chat / Std.: {:.1}</p><h2>Auswertung</h2><p>Die letzten {} Streams erreichten durchschnittlich {:.1} Zuschauer bei einem mittleren Peak von {recent_peak:.1}. Beste Kategorie nach Zuschauerdurchschnitt: <strong>{best}</strong>.</p><h2>Letzte Streams</h2><table><thead><tr><th>Start</th><th>Titel</th><th>Kategorie</th><th>Dauer</th><th>Peak</th><th>Ø</th><th>Follower</th><th>Chat</th></tr></thead><tbody>{table}</tbody></table></body></html>",rows.len(),numeric(&stats["peakViewers"]),rows.iter().map(|r|r["AverageViewers"].as_f64().unwrap_or(0.0)).fold(0.0,f64::max),h(&stats["totalDuration"]),numeric(&stats["followers"]),if hours>0.0{chat as f64/hours}else{0.0},recent.len(),avg)
}
