use serde_json::{json, Value};
use std::path::{Path, PathBuf};
use std::sync::Arc;
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

#[derive(Clone)]
pub struct OverlayLayoutStore {
    root: PathBuf,
    hub: Option<Arc<crate::RealtimeHub>>,
}
impl std::fmt::Debug for OverlayLayoutStore {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("OverlayLayoutStore")
            .field("root", &self.root)
            .finish_non_exhaustive()
    }
}

impl OverlayLayoutStore {
    pub fn new(root: impl Into<PathBuf>) -> Self {
        Self {
            root: root.into(),
            hub: None,
        }
    }
    pub fn with_hub(root: impl Into<PathBuf>, hub: Arc<crate::RealtimeHub>) -> Self {
        Self {
            root: root.into(),
            hub: Some(hub),
        }
    }
    pub async fn resolve_chat_capacity(&self) -> Result<usize, LayoutError> {
        let mut max_lines: i64 = 80;
        let mut files = match fs::read_dir(&self.root).await {
            Ok(files) => files,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(160),
            Err(error) => return Err(error.into()),
        };
        while let Some(file) = files.next_entry().await? {
            if !file.file_type().await?.is_file()
                || !file
                    .path()
                    .extension()
                    .and_then(|e| e.to_str())
                    .is_some_and(|e| e.eq_ignore_ascii_case("json"))
            {
                continue;
            }
            let bytes = fs::read(file.path()).await?;
            let Ok(layout) = serde_json::from_slice::<Value>(&bytes) else {
                continue;
            };
            for item in layout["items"].as_array().into_iter().flatten() {
                if !item["type"]
                    .as_str()
                    .is_some_and(|s| s.eq_ignore_ascii_case("chat"))
                {
                    continue;
                }
                let value = &item["props"]["maxLines"];
                let lines = value
                    .as_i64()
                    .filter(|n| i32::try_from(*n).is_ok())
                    .or_else(|| {
                        value
                            .as_str()
                            .and_then(|s| s.trim().parse::<i32>().ok())
                            .map(i64::from)
                    })
                    .unwrap_or(80);
                max_lines = max_lines.max(lines);
            }
        }
        Ok((max_lines * 2).clamp(0, 2000) as usize)
    }
    async fn refresh_capacity_unlocked(&self) -> Result<(), LayoutError> {
        if let Some(hub) = &self.hub {
            hub.configure_chat_buffer(self.resolve_chat_capacity().await?);
            hub.set_history_error("chatHistoryCapacityError", None);
        }
        Ok(())
    }
    pub async fn refresh_chat_capacity(&self) -> Result<(), LayoutError> {
        let _guard = if let Some(hub) = &self.hub {
            Some(hub.chat_layout_lock.lock().await)
        } else {
            None
        };
        self.refresh_capacity_unlocked().await
    }
    async fn notify_capacity(&self) {
        if let Err(error) = self.refresh_capacity_unlocked().await {
            // The layout was already saved. Preserve the previous usable capacity.
            tracing::warn!(%error, "Chat-Puffer konnte nicht aktualisiert werden");
            if let Some(hub) = &self.hub {
                hub.set_history_error(
                    "chatHistoryCapacityError",
                    Some(format!(
                        "Chat-Puffer konnte nicht aktualisiert werden: {error}"
                    )),
                );
            }
        }
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
        let path = self.layout_path(instance_id)?;
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
        let _guard = if let Some(hub) = &self.hub {
            Some(hub.chat_layout_lock.lock().await)
        } else {
            None
        };
        let path = self.layout_path(instance_id)?;
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).await?;
        }
        let json = serde_json::to_vec_pretty(layout)?;
        fs::write(&path, json).await?;
        self.notify_capacity().await;
        Ok(())
    }

    /// Update the current files under the same lock as editor saves and canvas changes.
    pub async fn synchronize_goals(&self, settings: &Value) -> Vec<String> {
        let _guard = if let Some(hub) = &self.hub {
            Some(hub.chat_layout_lock.lock().await)
        } else {
            None
        };
        let mut warnings = vec![];
        let mut files = match fs::read_dir(&self.root).await {
            Ok(files) => files,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return warnings,
            Err(error) => return vec![format!("Goal-Layouts nicht lesbar: {error}")],
        };
        loop {
            let file = match files.next_entry().await {
                Ok(Some(file)) => file,
                Ok(None) => break,
                Err(error) => {
                    warnings.push(format!("Goal-Layouts nicht lesbar: {error}"));
                    break;
                }
            };
            let path = file.path();
            if !path
                .extension()
                .and_then(|e| e.to_str())
                .is_some_and(|e| e.eq_ignore_ascii_case("json"))
            {
                continue;
            }
            let Some(id) = path.file_stem().and_then(|s| s.to_str()) else {
                continue;
            };
            let result:Result<(),String>=async {
                if !file.file_type().await.map_err(|e|e.to_string())?.is_file(){return Ok(());}
                let mut layout:Value=serde_json::from_slice(&fs::read(&path).await.map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
                if !layout.is_object(){return Err("Layout muss ein Objekt sein.".into());}
                let old=layout.clone();
                for item in layout["items"].as_array_mut().into_iter().flatten() {
                    if !item["type"].as_str().is_some_and(|s|s.eq_ignore_ascii_case("goal-bar")){continue;}
                    if !item["props"].is_object(){item["props"]=json!({});}
                    let props=item["props"].as_object_mut().unwrap();
                    let kind=props.iter().find(|(k,_)|k.eq_ignore_ascii_case("kind")).and_then(|(_,v)|v.as_str()).unwrap_or("followers").to_lowercase();
                    let (key,title,target)=match kind.as_str(){"subs"=>("SubGoal","Sub-Ziel",25),"bits"|"custom"=>("DonationGoal","Donation-Ziel",100),_=>("FollowerGoal","Follower-Ziel",200)};
                    let goal=&settings[key];
                    let title=goal["Title"].as_str().unwrap_or(title);
                    let reason=goal["Reason"].as_str().unwrap_or("");
                    let label=if key=="DonationGoal"&&!reason.trim().is_empty(){format!("{title} · {reason}")}else{title.into()};
                    props.retain(|key,_|!key.eq_ignore_ascii_case("current")&&!key.eq_ignore_ascii_case("label")&&!key.eq_ignore_ascii_case("target"));
                    props.insert("label".into(),json!(label));
                    props.insert("target".into(),json!(goal["Target"].as_f64().unwrap_or(target as f64).max(1.0)));
                }
                if old!=layout {
                    fs::write(&path,serde_json::to_vec_pretty(&layout).map_err(|e|e.to_string())?).await.map_err(|e|e.to_string())?;
                    if let Some(hub)=&self.hub {hub.publish(&json!({"source":"app","type":"app.overlay.layout","at":chrono::Utc::now().to_rfc3339(),"summary":"Ziele aktualisiert","data":{"instanceId":id,"layout":layout.to_string()}}));}
                }
                Ok(())
            }.await;
            if let Err(error) = result {
                warnings.push(format!(
                    "Goal-Layout {id} konnte nicht aktualisiert werden: {error}"
                ));
            }
        }
        warnings
    }

    pub async fn duplicate(&self, source_id: &str, target_id: &str) -> Result<(), LayoutError> {
        let layout = self.load(source_id).await?;
        self.save(target_id, &layout).await
    }

    pub async fn delete(&self, instance_id: &str) -> Result<(), LayoutError> {
        let _guard = if let Some(hub) = &self.hub {
            Some(hub.chat_layout_lock.lock().await)
        } else {
            None
        };
        let path = self.layout_path(instance_id)?;
        let result = match fs::remove_file(&path).await {
            Ok(()) => Ok(()),
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(err) => Err(err.into()),
        };
        if result.is_ok() {
            self.notify_capacity().await;
        }
        result
    }

    fn layout_path(&self, instance_id: &str) -> Result<PathBuf, LayoutError> {
        Ok(self
            .root
            .join(format!("{}.json", normalize_instance_id(instance_id)?)))
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
    async fn chat_capacity_uses_all_layouts_and_preserves_a_minimum_of_160() {
        let dir = tempdir().unwrap();
        let store = OverlayLayoutStore::new(dir.path());
        assert_eq!(store.resolve_chat_capacity().await.unwrap(), 160);
        for (value, expected) in [
            (json!(40), 160),
            (json!(120), 240),
            (json!(" 750 "), 1500),
            (json!(2000), 2000),
            (json!(i32::MAX), 2000),
            (json!(2147483648_i64), 160),
            (json!(12.5), 160),
            (json!("invalid"), 160),
        ] {
            store
                .save(
                    "main",
                    &json!({"items":[{"type":"CHAT","props":{"maxLines":value}}]}),
                )
                .await
                .unwrap();
            assert_eq!(store.resolve_chat_capacity().await.unwrap(), expected);
        }
        std::fs::write(dir.path().join("broken.json"), "corrupt").unwrap();
        std::fs::create_dir(dir.path().join("directory.json")).unwrap();
        store.save("other", &json!({"items":[{"type":"chat","props":{"maxLines":"300"}},{"type":"text","props":{"maxLines":2000}}]})).await.unwrap();
        assert_eq!(store.resolve_chat_capacity().await.unwrap(), 600);
    }

    #[tokio::test]
    async fn concurrent_layout_mutations_update_one_shared_chat_buffer() {
        let dir = tempdir().unwrap();
        let hub = Arc::new(crate::RealtimeHub::new());
        let store = OverlayLayoutStore::with_hub(dir.path(), hub.clone());
        let high = json!({"items":[{"type":"chat","props":{"maxLines":500}}]});
        let low = json!({"items":[{"type":"chat","props":{"maxLines":40}}]});
        let (one, two) = tokio::join!(store.save("high", &high), store.save("low", &low));
        one.unwrap();
        two.unwrap();
        assert_eq!(hub.chat_capacity(), 1000);
        store.delete("high").await.unwrap();
        assert_eq!(hub.chat_capacity(), 160);
    }

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
