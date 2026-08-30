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
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent).await?;
        }

        let mut clone = settings.clone();
        clone.schema_version = CURRENT_SCHEMA_VERSION;
        clone.overlay.ensure_canvases_migrated();
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
