use crate::layout_store::{LayoutError, OverlayLayoutStore};
use ccs_core::{AppSettings, JsonSettingsStore, OverlayCanvasSettings, OverlaySettings};
use serde_json::{json, Value};
use std::future::Future;

#[derive(Debug, thiserror::Error)]
pub enum CanvasError {
    #[error("{0}")]
    Message(String),
    #[error("{0}")]
    Layout(#[from] LayoutError),
}

pub trait CanvasSettingsPersist: Send + Sync {
    fn save(&self, settings: &AppSettings) -> impl Future<Output = Result<(), CanvasError>> + Send;
}

impl CanvasSettingsPersist for JsonSettingsStore {
    fn save(&self, settings: &AppSettings) -> impl Future<Output = Result<(), CanvasError>> + Send {
        let settings = settings.clone();
        async move {
            JsonSettingsStore::save(self, &settings)
                .await
                .map_err(|e| CanvasError::Message(e.to_string()))
        }
    }
}

pub trait OverlayLayoutOps: Send + Sync {
    fn load(&self, id: &str) -> impl Future<Output = Result<Value, CanvasError>> + Send;
    fn save(
        &self,
        id: &str,
        layout: &Value,
    ) -> impl Future<Output = Result<(), CanvasError>> + Send;
    fn duplicate(
        &self,
        source_id: &str,
        target_id: &str,
    ) -> impl Future<Output = Result<(), CanvasError>> + Send;
    fn delete(&self, id: &str) -> impl Future<Output = Result<(), CanvasError>> + Send;
    fn exists(&self, id: &str) -> bool;
}

impl OverlayLayoutOps for OverlayLayoutStore {
    fn load(&self, id: &str) -> impl Future<Output = Result<Value, CanvasError>> + Send {
        let id = id.to_string();
        async move {
            OverlayLayoutStore::load(self, &id)
                .await
                .map_err(Into::into)
        }
    }

    fn save(
        &self,
        id: &str,
        layout: &Value,
    ) -> impl Future<Output = Result<(), CanvasError>> + Send {
        let id = id.to_string();
        let layout = layout.clone();
        async move {
            OverlayLayoutStore::save(self, &id, &layout)
                .await
                .map_err(Into::into)
        }
    }

    fn duplicate(
        &self,
        source_id: &str,
        target_id: &str,
    ) -> impl Future<Output = Result<(), CanvasError>> + Send {
        let source_id = source_id.to_string();
        let target_id = target_id.to_string();
        async move {
            OverlayLayoutStore::duplicate(self, &source_id, &target_id)
                .await
                .map_err(Into::into)
        }
    }

    fn delete(&self, id: &str) -> impl Future<Output = Result<(), CanvasError>> + Send {
        let id = id.to_string();
        async move {
            OverlayLayoutStore::delete(self, &id)
                .await
                .map_err(Into::into)
        }
    }

    fn exists(&self, id: &str) -> bool {
        OverlayLayoutStore::exists(self, id)
    }
}

pub struct OverlayCanvasService<L> {
    layouts: L,
}

impl<L: OverlayLayoutOps> OverlayCanvasService<L> {
    pub fn new(layouts: L) -> Self {
        Self { layouts }
    }

    pub async fn create<S: CanvasSettingsPersist>(
        &self,
        settings: &mut AppSettings,
        persist: &S,
        name: &str,
    ) -> Result<OverlayCanvasSettings, CanvasError> {
        let normalized_name = normalize_name(name)?;
        settings.overlay.ensure_canvases_migrated();
        let previous_selected = settings.overlay.selected_canvas_id.clone();
        let id = OverlaySettings::create_canvas_id(
            &normalized_name,
            settings.overlay.canvases.iter().map(|c| c.id.as_str()),
        );
        let canvas = OverlayCanvasSettings {
            id: id.clone(),
            name: normalized_name.clone(),
        };
        let layout = OverlayLayoutStore::default_layout(&normalized_name);
        self.layouts.save(&id, &layout).await?;
        settings.overlay.canvases.push(canvas.clone());
        settings.overlay.selected_canvas_id = id.clone();

        match persist.save(settings).await {
            Ok(()) => Ok(canvas),
            Err(err) => {
                settings.overlay.canvases.retain(|c| c.id != id);
                settings.overlay.selected_canvas_id = previous_selected;
                let _ = self.layouts.delete(&id).await;
                Err(err)
            }
        }
    }

    pub async fn duplicate<S: CanvasSettingsPersist>(
        &self,
        settings: &mut AppSettings,
        persist: &S,
        source_id: &str,
        name: &str,
    ) -> Result<OverlayCanvasSettings, CanvasError> {
        let normalized_name = normalize_name(name)?;
        settings.overlay.ensure_canvases_migrated();
        let source = find_canvas(settings, source_id)?.clone();
        let previous_selected = settings.overlay.selected_canvas_id.clone();
        let id = OverlaySettings::create_canvas_id(
            &normalized_name,
            settings.overlay.canvases.iter().map(|c| c.id.as_str()),
        );
        let duplicate = OverlayCanvasSettings {
            id: id.clone(),
            name: normalized_name.clone(),
        };

        self.layouts.duplicate(&source.id, &id).await?;
        let mut layout = self.layouts.load(&id).await?;
        if let Some(obj) = layout.as_object_mut() {
            obj.insert("name".into(), json!(normalized_name));
        }
        self.layouts.save(&id, &layout).await?;
        settings.overlay.canvases.push(duplicate.clone());
        settings.overlay.selected_canvas_id = id.clone();

        match persist.save(settings).await {
            Ok(()) => Ok(duplicate),
            Err(err) => {
                settings.overlay.canvases.retain(|c| c.id != id);
                settings.overlay.selected_canvas_id = previous_selected;
                let _ = self.layouts.delete(&id).await;
                Err(err)
            }
        }
    }

    pub async fn delete<S: CanvasSettingsPersist>(
        &self,
        settings: &mut AppSettings,
        persist: &S,
        canvas_id: &str,
    ) -> Result<(), CanvasError> {
        settings.overlay.ensure_canvases_migrated();
        if settings.overlay.canvases.len() <= 1 {
            return Err(CanvasError::Message(
                "Das letzte Canvas kann nicht gelöscht werden.".into(),
            ));
        }

        let index = find_canvas_index(settings, canvas_id)?;
        let previous_selected = settings.overlay.selected_canvas_id.clone();
        let canvas = settings.overlay.canvases.remove(index);
        settings.overlay.ensure_canvases_migrated();

        match persist.save(settings).await {
            Ok(()) => {
                if let Err(err) = self.layouts.delete(&canvas.id).await {
                    tracing::warn!(
                        canvas_id = %canvas.id,
                        error = %err,
                        "orphaned canvas layout could not be removed"
                    );
                }
                Ok(())
            }
            Err(err) => {
                settings.overlay.canvases.insert(index, canvas);
                settings.overlay.selected_canvas_id = previous_selected;
                Err(err)
            }
        }
    }
}

fn normalize_name(name: &str) -> Result<String, CanvasError> {
    let normalized = name.trim().to_string();
    if normalized.is_empty() {
        return Err(CanvasError::Message(
            "Der Canvas-Name darf nicht leer sein.".into(),
        ));
    }
    Ok(normalized)
}

fn find_canvas_index(settings: &AppSettings, canvas_id: &str) -> Result<usize, CanvasError> {
    settings
        .overlay
        .canvases
        .iter()
        .position(|c| c.id.eq_ignore_ascii_case(canvas_id))
        .ok_or_else(|| {
            CanvasError::Message(format!(
                "Overlay-Canvas '{canvas_id}' wurde nicht gefunden."
            ))
        })
}

fn find_canvas<'a>(
    settings: &'a AppSettings,
    canvas_id: &str,
) -> Result<&'a OverlayCanvasSettings, CanvasError> {
    let index = find_canvas_index(settings, canvas_id)?;
    Ok(&settings.overlay.canvases[index])
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    use std::collections::HashMap;
    use std::sync::Mutex;
    use tempfile::tempdir;

    struct FakePersist {
        events: Mutex<Vec<String>>,
        fail: bool,
    }

    impl FakePersist {
        fn ok() -> Self {
            Self {
                events: Mutex::new(vec![]),
                fail: false,
            }
        }

        fn failing() -> Self {
            Self {
                events: Mutex::new(vec![]),
                fail: true,
            }
        }

        fn events(&self) -> Vec<String> {
            self.events.lock().unwrap().clone()
        }
    }

    impl CanvasSettingsPersist for FakePersist {
        fn save(
            &self,
            _settings: &AppSettings,
        ) -> impl Future<Output = Result<(), CanvasError>> + Send {
            async move {
                self.events.lock().unwrap().push("save".into());
                if self.fail {
                    Err(CanvasError::Message("disk full".into()))
                } else {
                    Ok(())
                }
            }
        }
    }

    struct TrackingLayoutStore {
        layouts: Mutex<HashMap<String, Value>>,
        events: Mutex<Vec<String>>,
    }

    impl TrackingLayoutStore {
        fn new() -> Self {
            Self {
                layouts: Mutex::new(HashMap::new()),
                events: Mutex::new(vec![]),
            }
        }

        fn events(&self) -> Vec<String> {
            self.events.lock().unwrap().clone()
        }
    }

    impl OverlayLayoutOps for &TrackingLayoutStore {
        fn load(&self, id: &str) -> impl Future<Output = Result<Value, CanvasError>> + Send {
            let id = id.to_string();
            async move {
                Ok(self
                    .layouts
                    .lock()
                    .unwrap()
                    .get(&id)
                    .cloned()
                    .unwrap_or_else(|| OverlayLayoutStore::default_layout("")))
            }
        }

        fn save(
            &self,
            id: &str,
            layout: &Value,
        ) -> impl Future<Output = Result<(), CanvasError>> + Send {
            let id = id.to_string();
            let layout = layout.clone();
            async move {
                self.layouts.lock().unwrap().insert(id, layout);
                Ok(())
            }
        }

        fn duplicate(
            &self,
            source_id: &str,
            target_id: &str,
        ) -> impl Future<Output = Result<(), CanvasError>> + Send {
            let source_id = source_id.to_string();
            let target_id = target_id.to_string();
            async move {
                let source = self
                    .layouts
                    .lock()
                    .unwrap()
                    .get(&source_id)
                    .cloned()
                    .unwrap_or_else(|| OverlayLayoutStore::default_layout(""));
                self.layouts.lock().unwrap().insert(target_id, source);
                Ok(())
            }
        }

        fn delete(&self, id: &str) -> impl Future<Output = Result<(), CanvasError>> + Send {
            let id = id.to_string();
            async move {
                self.events.lock().unwrap().push(format!("delete:{id}"));
                self.layouts.lock().unwrap().remove(&id);
                Ok(())
            }
        }

        fn exists(&self, id: &str) -> bool {
            self.layouts.lock().unwrap().contains_key(id)
        }
    }

    fn two_canvas_settings() -> AppSettings {
        let mut settings = AppSettings::default();
        settings.overlay.canvases = vec![
            OverlayCanvasSettings {
                id: "first".into(),
                name: "First".into(),
            },
            OverlayCanvasSettings {
                id: "second".into(),
                name: "Second".into(),
            },
        ];
        settings.overlay.selected_canvas_id = "second".into();
        settings
    }

    #[tokio::test]
    async fn create_persists_default_layout_and_selects_canvas() {
        let dir = tempdir().unwrap();
        let layouts = OverlayLayoutStore::new(dir.path());
        let service = OverlayCanvasService::new(layouts.clone());
        let persist = FakePersist::ok();
        let mut settings = AppSettings::default();
        settings.overlay.ensure_canvases_migrated();

        let canvas = service
            .create(&mut settings, &persist, " My Canvas ")
            .await
            .unwrap();

        assert_eq!(canvas.id, "my-canvas");
        assert_eq!(canvas.name, "My Canvas");
        assert_eq!(settings.overlay.selected_canvas_id, canvas.id);
        assert!(layouts.exists(&canvas.id));
        let layout = layouts.load(&canvas.id).await.unwrap();
        assert_eq!(layout["name"], "My Canvas");
        assert_eq!(layout["canvasWidth"], 1920);
        assert_eq!(layout["canvasHeight"], 1080);
        assert_eq!(persist.events(), ["save"]);
    }

    #[tokio::test]
    async fn duplicate_copies_layout_and_uses_unique_id() {
        let dir = tempdir().unwrap();
        let layouts = OverlayLayoutStore::new(dir.path());
        let mut source_layout = OverlayLayoutStore::default_layout("Source");
        source_layout["items"] = json!([{ "id": "contract-item", "type": "text" }]);
        layouts.save("source", &source_layout).await.unwrap();

        let mut settings = AppSettings::default();
        settings.overlay.canvases = vec![
            OverlayCanvasSettings {
                id: "source".into(),
                name: "Source".into(),
            },
            OverlayCanvasSettings {
                id: "source-kopie".into(),
                name: "Existing".into(),
            },
        ];
        settings.overlay.selected_canvas_id = "source".into();

        let service = OverlayCanvasService::new(layouts.clone());
        let persist = FakePersist::ok();
        let duplicate = service
            .duplicate(&mut settings, &persist, "source", "Source Kopie")
            .await
            .unwrap();

        assert_eq!(duplicate.id, "source-kopie-2");
        assert_eq!(duplicate.name, "Source Kopie");
        let copied = layouts.load(&duplicate.id).await.unwrap();
        assert_eq!(copied["name"], "Source Kopie");
        assert_eq!(copied["items"][0]["id"], "contract-item");
    }

    #[tokio::test]
    async fn delete_persists_metadata_before_removing_layout() {
        let layouts = TrackingLayoutStore::new();
        layouts.layouts.lock().unwrap().insert(
            "second".into(),
            OverlayLayoutStore::default_layout("Second"),
        );
        let persist = FakePersist::ok();
        let mut settings = two_canvas_settings();
        let service = OverlayCanvasService::new(&layouts);

        service
            .delete(&mut settings, &persist, "second")
            .await
            .unwrap();

        assert!(!settings.overlay.canvases.iter().any(|c| c.id == "second"));
        assert_eq!(settings.overlay.selected_canvas_id, "first");
        let mut events = persist.events();
        events.extend(layouts.events());
        assert_eq!(events, ["save", "delete:second"]);
    }

    #[tokio::test]
    async fn delete_rejects_last_canvas() {
        let layouts = OverlayLayoutStore::new(tempdir().unwrap().path());
        let service = OverlayCanvasService::new(layouts);
        let persist = FakePersist::ok();
        let mut settings = AppSettings::default();
        settings.overlay.ensure_canvases_migrated();
        let id = settings.overlay.canvases[0].id.clone();

        let err = service
            .delete(&mut settings, &persist, &id)
            .await
            .unwrap_err();
        assert!(err.to_string().contains("letzte"));
    }

    #[tokio::test]
    async fn create_rolls_back_metadata_and_layout_when_settings_save_fails() {
        let dir = tempdir().unwrap();
        let layouts = OverlayLayoutStore::new(dir.path());
        let service = OverlayCanvasService::new(layouts.clone());
        let persist = FakePersist::failing();
        let mut settings = AppSettings::default();
        settings.overlay.ensure_canvases_migrated();
        let original_selected = settings.overlay.selected_canvas_id.clone();

        let err = service
            .create(&mut settings, &persist, "Rollback")
            .await
            .unwrap_err();
        assert!(err.to_string().contains("disk full"));
        assert!(!settings.overlay.canvases.iter().any(|c| c.id == "rollback"));
        assert_eq!(settings.overlay.selected_canvas_id, original_selected);
        assert!(!layouts.exists("rollback"));
    }

    #[tokio::test]
    async fn duplicate_rolls_back_layout_when_settings_save_fails() {
        let dir = tempdir().unwrap();
        let layouts = OverlayLayoutStore::new(dir.path());
        layouts
            .save("source", &OverlayLayoutStore::default_layout("Source"))
            .await
            .unwrap();
        let mut settings = AppSettings::default();
        settings.overlay.canvases = vec![OverlayCanvasSettings {
            id: "source".into(),
            name: "Source".into(),
        }];
        settings.overlay.selected_canvas_id = "source".into();
        let persist = FakePersist::failing();
        let service = OverlayCanvasService::new(layouts.clone());

        let err = service
            .duplicate(&mut settings, &persist, "source", "Source Kopie")
            .await
            .unwrap_err();
        assert!(err.to_string().contains("disk full"));
        assert_eq!(settings.overlay.canvases.len(), 1);
        assert_eq!(settings.overlay.selected_canvas_id, "source");
        assert!(!layouts.exists("source-kopie"));
    }
}
