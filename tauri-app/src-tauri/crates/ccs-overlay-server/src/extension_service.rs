use crate::{library::PACK_LIMIT, MediaLibrary, RealtimeHub};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    io::Read,
    path::{Path, PathBuf},
    sync::Arc,
};

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum ExtensionPackAction {
    Installed,
    Uninstalled,
    Refresh,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExtensionPackChange {
    pub action: ExtensionPackAction,
    pub pack_id: String,
}

/// The native commands and Canvas HTTP routes use the same operations and events.
pub struct ExtensionPackService {
    root: PathBuf,
    hub: Arc<RealtimeHub>,
}
impl ExtensionPackService {
    pub fn new(root: impl AsRef<Path>, hub: Arc<RealtimeHub>) -> Self {
        Self {
            root: root.as_ref().into(),
            hub,
        }
    }
    async fn run<T: Send + 'static>(
        &self,
        operation: impl FnOnce(MediaLibrary) -> Result<T, String> + Send + 'static,
    ) -> Result<T, String> {
        let root = self.root.clone();
        tokio::task::spawn_blocking(move || operation(MediaLibrary::new(root)))
            .await
            .map_err(|e| e.to_string())?
    }
    pub async fn list(&self) -> Result<Vec<Value>, String> {
        self.run(|library| library.packs()).await
    }
    pub async fn install(&self, data: Vec<u8>) -> Result<Value, String> {
        let installed = self.run(move |library| library.install_pack(&data)).await?;
        self.changed(
            ExtensionPackAction::Installed,
            installed["id"].as_str().unwrap(),
        );
        Ok(installed)
    }
    pub async fn import(&self, path: PathBuf) -> Result<Value, String> {
        let installed = self
            .run(move |library| {
                let info = std::fs::metadata(&path).map_err(|e| e.to_string())?;
                if !info.is_file() {
                    return Err("ZIP-Datei erwartet".into());
                }
                if info.len() > PACK_LIMIT as u64 {
                    return Err("ZIP überschreitet 50 MB".into());
                }
                let mut bytes = Vec::new();
                std::fs::File::open(path)
                    .map_err(|e| e.to_string())?
                    .take(PACK_LIMIT as u64 + 1)
                    .read_to_end(&mut bytes)
                    .map_err(|e| e.to_string())?;
                library.install_pack(&bytes)
            })
            .await?;
        self.changed(
            ExtensionPackAction::Installed,
            installed["id"].as_str().unwrap(),
        );
        Ok(installed)
    }
    pub async fn uninstall(&self, id: String) -> Result<(), String> {
        let target = id.clone();
        self.run(move |library| library.delete_pack(&target))
            .await?;
        self.changed(ExtensionPackAction::Uninstalled, &id);
        Ok(())
    }
    fn changed(&self, action: ExtensionPackAction, id: &str) {
        self.hub.extension_changed(ExtensionPackChange {
            action,
            pack_id: id.into(),
        });
    }
}
