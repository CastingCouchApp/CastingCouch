use crate::settings::{AppSettings, CURRENT_SCHEMA_VERSION};
use serde_json::{Map, Value};
use std::path::{Path, PathBuf};
use tokio::fs;
use tokio::io::AsyncWriteExt;
use tokio::sync::Mutex;

#[derive(Debug, thiserror::Error)]
pub enum SettingsError {
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    #[error("json: {0}")]
    Json(#[from] serde_json::Error),
    #[error("unsupported settings schema {0}; current is {CURRENT_SCHEMA_VERSION}")]
    UnsupportedSchema(u32),
    #[error("Einstellungen wurden zwischenzeitlich geändert: {0}. Bitte neu laden.")]
    Conflict(String),
    #[error("Ungültige Einstellungen: {0}")]
    Validation(String),
}

pub struct JsonSettingsStore {
    path: PathBuf,
    save_lock: Mutex<()>,
}

impl JsonSettingsStore {
    pub fn new(path: impl Into<PathBuf>) -> Self {
        Self {
            path: path.into(),
            save_lock: Mutex::new(()),
        }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub async fn load(&self) -> Result<AppSettings, SettingsError> {
        if !self.path.exists() {
            let defaults = AppSettings::default();
            self.save(&defaults).await?;
            return Ok(defaults);
        }

        let bytes = fs::read(&self.path).await?;
        let mut root: Value = serde_json::from_slice(&bytes)?;
        let migrated = migrate(&mut root)?;
        let mut settings: AppSettings = serde_json::from_value(root)?;
        settings.overlay.ensure_canvases_migrated();
        settings.schema_version = CURRENT_SCHEMA_VERSION;
        if migrated {
            self.save(&settings).await?;
        }
        Ok(settings)
    }

    pub async fn save(&self, settings: &AppSettings) -> Result<(), SettingsError> {
        let _guard = self.save_lock.lock().await;
        let mut next = serde_json::to_value(settings)?;
        if self.path.exists() {
            let raw: Value = serde_json::from_slice(&fs::read(&self.path).await?)?;
            let known = serde_json::to_value(serde_json::from_value::<AppSettings>(raw.clone())?)?;
            preserve_unknown(&raw, &known, &mut next, true);
        }
        self.write_value(&next).await
    }

    pub async fn read_value(&self) -> Result<Value, SettingsError> {
        let known = serde_json::to_value(self.load().await?)?;
        let raw: Value = serde_json::from_slice(&fs::read(&self.path).await?)?;
        let mut result = known.clone();
        preserve_unknown(&raw, &known, &mut result, false);
        Ok(result)
    }

    pub async fn save_edit(
        &self,
        original: &Value,
        edited: &Value,
    ) -> Result<Value, SettingsError> {
        let _guard = self.save_lock.lock().await;
        // load() is not used while holding the write lock: it may perform a migration.
        let raw: Value = serde_json::from_slice(&fs::read(&self.path).await?)?;
        let known = serde_json::to_value(serde_json::from_value::<AppSettings>(raw.clone())?)?;
        let mut current = known.clone();
        preserve_unknown(&raw, &known, &mut current, false);
        merge_edit(original, edited, &mut current, "")?;
        let settings: AppSettings = serde_json::from_value(current.clone())?;
        validate_settings(&settings)?;
        preserve_unknown(&raw, &known, &mut current, true);
        self.write_value(&current).await?;
        Ok(current)
    }

    async fn write_value(&self, value: &Value) -> Result<(), SettingsError> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent).await?;
        }
        let mut clone = value.clone();
        clone["SchemaVersion"] = Value::from(CURRENT_SCHEMA_VERSION);
        let json = serde_json::to_vec_pretty(&clone)?;

        let tmp = self
            .path
            .with_extension(format!("{}.tmp", uuid::Uuid::new_v4().simple()));
        {
            let mut file = fs::File::create(&tmp).await?;
            file.write_all(&json).await?;
            file.flush().await?;
        }

        if self.path.exists() {
            let bak = PathBuf::from(format!("{}.bak", self.path.display()));
            let _ = fs::copy(&self.path, &bak).await;
        }

        fs::rename(&tmp, &self.path).await?;
        Ok(())
    }
}

fn preserve_unknown(raw: &Value, known: &Value, next: &mut Value, representation: bool) {
    // Keep legacy enum/number representation when a typed save did not edit its value.
    if representation && !known.is_object() && !known.is_array() && next == known {
        *next = raw.clone();
        return;
    }
    if let (Some(raw), Some(known), Some(next)) =
        (raw.as_array(), known.as_array(), next.as_array_mut())
    {
        for (index, new) in next.iter_mut().enumerate() {
            // Identified records may move or disappear; never transfer extras to a different record.
            let identity = new.get("Id").or_else(|| new.get("id")).cloned();
            let old_index = match identity {
                Some(id) => known
                    .iter()
                    .position(|old| old.get("Id").or_else(|| old.get("id")) == Some(&id)),
                None => (index < known.len()).then_some(index),
            };
            if let Some(i) = old_index {
                if let (Some(original), Some(old)) = (raw.get(i), known.get(i)) {
                    preserve_unknown(original, old, new, representation);
                }
            }
        }
        return;
    }
    if let (Some(raw), Some(known), Some(next)) =
        (raw.as_object(), known.as_object(), next.as_object_mut())
    {
        for (key, value) in raw {
            if let Some(old) = known.get(key) {
                if let Some(new) = next.get_mut(key) {
                    preserve_unknown(value, old, new, representation);
                }
            } else {
                next.entry(key.clone()).or_insert_with(|| value.clone());
            }
        }
    }
}

fn merge_edit(
    base: &Value,
    edited: &Value,
    current: &mut Value,
    path: &str,
) -> Result<(), SettingsError> {
    if base == edited {
        return Ok(());
    }
    if let (Some(base), Some(edited), Some(current)) = (
        base.as_object(),
        edited.as_object(),
        current.as_object_mut(),
    ) {
        for (key, next) in edited {
            let previous = base.get(key).unwrap_or(&Value::Null);
            merge_edit(
                previous,
                next,
                current.entry(key.clone()).or_insert(Value::Null),
                &format!("{path}/{key}"),
            )?;
        }
        for (key, previous) in base {
            if !edited.contains_key(key) {
                if current.get(key).is_some_and(|v| v != previous) {
                    return Err(SettingsError::Conflict(format!("{path}/{key}")));
                }
                current.remove(key);
            }
        }
    } else if current == base || current == edited {
        *current = edited.clone();
    } else {
        return Err(SettingsError::Conflict(path.to_string()));
    }
    Ok(())
}

pub fn validate_settings(settings: &AppSettings) -> Result<(), SettingsError> {
    if settings.overlay.web_server_port == 0 || settings.obs.port == 0 {
        return Err(SettingsError::Validation(
            "Port muss zwischen 1 und 65535 liegen.".into(),
        ));
    }
    if settings.obs.host.trim().is_empty() || settings.general.connection_watchdog_seconds < 1 {
        return Err(SettingsError::Validation(
            "OBS-Host und positives Wiederverbindungsintervall sind erforderlich.".into(),
        ));
    }
    Ok(())
}

/// Sequential schema migrations, matching the WPF SettingsSchemaMigrator.
pub fn migrate(root: &mut Value) -> Result<bool, SettingsError> {
    let obj = root.as_object_mut().ok_or_else(|| {
        SettingsError::Json(serde_json::Error::io(std::io::Error::other(
            "root is not an object",
        )))
    })?;

    let version = obj
        .get("SchemaVersion")
        .and_then(Value::as_u64)
        .unwrap_or(0) as u32;

    if version > CURRENT_SCHEMA_VERSION {
        return Err(SettingsError::UnsupportedSchema(version));
    }

    let mut changed = false;
    if version < 1 {
        migrate_v0_to_v1(obj);
        changed = true;
    }
    if version < 2 {
        migrate_v1_to_v2(obj);
        changed = true;
    }
    obj.insert("SchemaVersion".into(), Value::from(CURRENT_SCHEMA_VERSION));
    Ok(changed)
}

fn migrate_v0_to_v1(obj: &mut Map<String, Value>) {
    if !obj.contains_key("SchemaVersion") {
        obj.insert("SchemaVersion".into(), Value::from(1));
    }
}

fn migrate_v1_to_v2(obj: &mut Map<String, Value>) {
    if let Some(overlay) = obj.get_mut("Overlay").and_then(Value::as_object_mut) {
        if !overlay.contains_key("Canvases") {
            overlay.insert(
                "Canvases".into(),
                serde_json::json!([{ "Id": "default", "Name": "Canvas" }]),
            );
        }
        if !overlay.contains_key("SelectedCanvasId") {
            overlay.insert("SelectedCanvasId".into(), Value::from("default"));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[tokio::test]
    async fn roundtrip_defaults() {
        let dir = tempdir().unwrap();
        let store = JsonSettingsStore::new(dir.path().join("settings.json"));
        let loaded = store.load().await.unwrap();
        assert_eq!(loaded.schema_version, CURRENT_SCHEMA_VERSION);
        assert!(store.path().exists());
    }

    #[test]
    fn rejects_future_schema() {
        let mut root = serde_json::json!({ "SchemaVersion": 99 });
        let err = migrate(&mut root).unwrap_err();
        assert!(matches!(err, SettingsError::UnsupportedSchema(99)));
    }

    #[test]
    fn migrates_v1_canvases() {
        let mut root = serde_json::json!({
            "SchemaVersion": 1,
            "Overlay": { "WebServerPort": 8765 }
        });
        assert!(migrate(&mut root).unwrap());
        let canvases = root["Overlay"]["Canvases"].as_array().unwrap();
        assert_eq!(canvases[0]["Id"], "default");
        assert_eq!(root["SchemaVersion"], CURRENT_SCHEMA_VERSION);
    }

    #[tokio::test]
    async fn alert_definitions_roundtrip_pascal_case() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("settings.json");
        std::fs::write(
            &path,
            serde_json::to_vec_pretty(&serde_json::json!({
                "SchemaVersion": 2,
                "Alerts": {
                    "Enabled": true,
                    "ObsSceneName": "alerts-live",
                    "Definitions": {
                        "Follow": {
                            "Type": "Follow",
                            "Enabled": true,
                            "TextTemplate": "{user} folgt jetzt!",
                            "DurationSeconds": 8,
                            "Priority": 100
                        },
                        "Cheer": {
                            "Type": "Cheer",
                            "Enabled": false,
                            "TextTemplate": "{user} cheeret {bits} Bits!",
                            "DurationSeconds": 9,
                            "Priority": 85
                        }
                    }
                }
            }))
            .unwrap(),
        )
        .unwrap();

        let store = JsonSettingsStore::new(&path);
        let loaded = store.load().await.unwrap();
        assert_eq!(loaded.alerts.obs_scene_name, "alerts-live");
        assert_eq!(
            loaded.alerts.definitions["Follow"].text_template,
            "{user} folgt jetzt!"
        );
        assert!(!loaded.alerts.definitions["Cheer"].enabled);

        store.save(&loaded).await.unwrap();
        let reloaded = JsonSettingsStore::new(&path).load().await.unwrap();
        assert_eq!(
            reloaded.alerts.definitions["Follow"].text_template,
            "{user} folgt jetzt!"
        );
        assert!(!reloaded.alerts.definitions["Cheer"].enabled);
        assert_eq!(reloaded.alerts.obs_scene_name, "alerts-live");

        let disk: serde_json::Value =
            serde_json::from_slice(&std::fs::read(&path).unwrap()).unwrap();
        assert_eq!(
            disk["Alerts"]["Definitions"]["Follow"]["TextTemplate"],
            "{user} folgt jetzt!"
        );
        assert_eq!(disk["Alerts"]["ObsSceneName"], "alerts-live");
    }

    #[tokio::test]
    async fn load_accepts_wpf_twitch_enum_integers() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("settings.json");
        std::fs::write(
            &path,
            serde_json::to_vec_pretty(&serde_json::json!({
                "SchemaVersion": 2,
                "Twitch": {
                    "ChatUiMode": 1,
                    "StreamEndMode": 2
                }
            }))
            .unwrap(),
        )
        .unwrap();

        let loaded = JsonSettingsStore::new(&path).load().await.unwrap();
        assert_eq!(loaded.twitch.chat_ui_mode, "EmbeddedWeb");
        assert_eq!(loaded.twitch.extra["StreamEndMode"], 2);
    }

    #[tokio::test]
    async fn sidecar_enabled_roundtrip() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("settings.json");
        std::fs::write(
            &path,
            serde_json::to_vec_pretty(&serde_json::json!({
                "SchemaVersion": 2,
                "Sidecar": {
                    "Enabled": true,
                    "Port": 18765,
                    "BinaryPath": "C:/Tools/CommandClient.exe"
                }
            }))
            .unwrap(),
        )
        .unwrap();

        let store = JsonSettingsStore::new(&path);
        let loaded = store.load().await.unwrap();
        assert!(loaded.sidecar.enabled);
        assert_eq!(loaded.sidecar.port, 18765);
        assert_eq!(loaded.sidecar.binary_path, "C:/Tools/CommandClient.exe");

        store.save(&loaded).await.unwrap();
        let disk: serde_json::Value =
            serde_json::from_slice(&std::fs::read(&path).unwrap()).unwrap();
        assert_eq!(disk["Sidecar"]["Enabled"], true);
        assert_eq!(disk["Sidecar"]["Port"], 18765);
        assert_eq!(disk["Sidecar"]["BinaryPath"], "C:/Tools/CommandClient.exe");
    }
}
