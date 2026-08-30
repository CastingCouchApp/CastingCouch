use serde_json::{json, Value};
use std::path::{Path, PathBuf};
use tokio::fs;

#[derive(Debug, thiserror::Error)]
pub enum LayoutError {
    #[error("Ungültige Overlay-Instanz-ID.")]
    InvalidId,
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    #[error("json: {0}")]
    Json(#[from] serde_json::Error),
}

#[derive(Debug, Clone)]
pub struct OverlayLayoutStore {
    root: PathBuf,
}

impl OverlayLayoutStore {
    pub fn new(root: impl Into<PathBuf>) -> Self {
        Self { root: root.into() }
    }

    pub fn root(&self) -> &Path {
        &self.root
    }

    pub fn default_layout(name: &str) -> Value {
        json!({
            "version": 1,
            "name": name,
            "canvasWidth": 1920,
            "canvasHeight": 1080,
            "items": []
        })
    }

    pub fn exists(&self, instance_id: &str) -> bool {
        self.layout_path(instance_id)
            .map(|path| path.exists())
            .unwrap_or(false)
    }

    pub async fn read_bytes(&self, instance_id: &str) -> Result<Option<Vec<u8>>, LayoutError> {
        let path = match self.layout_path(instance_id) {
            Ok(path) => path,
            Err(LayoutError::InvalidId) => return Ok(None),
            Err(err) => return Err(err),
        };
        match fs::read(&path).await {
            Ok(bytes) => Ok(Some(bytes)),
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(err) => Err(err.into()),
        }
    }

    pub async fn load(&self, instance_id: &str) -> Result<Value, LayoutError> {
        match self.read_bytes(instance_id).await? {
            Some(bytes) => match serde_json::from_slice(&bytes) {
                Ok(value) => Ok(value),
                Err(_) => Ok(Self::default_layout("")),
            },
            None => Ok(Self::default_layout("")),
        }
    }

    pub async fn save(&self, instance_id: &str, layout: &Value) -> Result<(), LayoutError> {
        let path = self.layout_path(instance_id)?;
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).await?;
        }
        let json = serde_json::to_vec_pretty(layout)?;
        fs::write(&path, json).await?;
        Ok(())
    }

    pub async fn duplicate(&self, source_id: &str, target_id: &str) -> Result<(), LayoutError> {
        let layout = self.load(source_id).await?;
        self.save(target_id, &layout).await
    }

    pub async fn delete(&self, instance_id: &str) -> Result<(), LayoutError> {
        let path = self.layout_path(instance_id)?;
        match fs::remove_file(&path).await {
            Ok(()) => Ok(()),
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(err) => Err(err.into()),
        }
    }

    fn layout_path(&self, instance_id: &str) -> Result<PathBuf, LayoutError> {
        Ok(self.root.join(format!("{}.json", normalize_instance_id(instance_id)?)))
    }
}

fn normalize_instance_id(instance_id: &str) -> Result<&str, LayoutError> {
    let id = instance_id.trim();
    if id.is_empty()
        || !id
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-')
    {
        return Err(LayoutError::InvalidId);
    }
    Ok(id)
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[tokio::test]
    async fn duplicate_copies_layout_file() {
        let dir = tempdir().unwrap();
        let store = OverlayLayoutStore::new(dir.path());
        let mut layout = OverlayLayoutStore::default_layout("Source");
        layout["items"] = json!([{ "id": "contract-item", "type": "text" }]);
        store.save("source", &layout).await.unwrap();

        store.duplicate("source", "copy").await.unwrap();
        let copied = store.load("copy").await.unwrap();
        assert_eq!(copied["items"][0]["id"], "contract-item");
        assert!(store.exists("copy"));
    }

    #[tokio::test]
    async fn save_rejects_unsafe_id() {
        let dir = tempdir().unwrap();
        let store = OverlayLayoutStore::new(dir.path());
        let err = store
            .save("../evil", &OverlayLayoutStore::default_layout("x"))
            .await
            .unwrap_err();
        assert!(matches!(err, LayoutError::InvalidId));
    }
}
