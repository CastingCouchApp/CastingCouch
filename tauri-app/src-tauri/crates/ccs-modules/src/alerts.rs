use crate::overlay_bridge::OverlayEventBridge;
use crate::twitch::alert_type_for_event;
use ccs_core::{AlertDefinitionSettings, AppSettings, JsonSettingsStore};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, HashMap, VecDeque};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::{Mutex, Notify};
use tokio::task::JoinHandle;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AlertDefinition {
    #[serde(default, rename = "type")]
    pub type_name: String,
    #[serde(default = "default_enabled")]
    pub enabled: bool,
    #[serde(default)]
    pub text_template: String,
    #[serde(default)]
    pub media_path: String,
    #[serde(default)]
    pub sound_path: String,
    #[serde(default = "default_duration_seconds")]
    pub duration_seconds: i32,
    #[serde(default = "default_priority")]
    pub priority: i32,
    #[serde(default = "default_font_face")]
    pub font_face: String,
    #[serde(default = "default_font_size")]
    pub font_size: i32,
    #[serde(default = "default_font_color")]
    pub font_color: String,
    #[serde(default = "default_animation")]
    pub animation: String,
    #[serde(default = "default_alert_x")]
    pub x: i32,
    #[serde(default = "default_alert_y")]
    pub y: i32,
    #[serde(default = "default_alert_width")]
    pub width: i32,
    #[serde(default = "default_alert_height")]
    pub height: i32,
    #[serde(default = "default_volume_percent")]
    pub volume_percent: i32,
    #[serde(default)]
    pub sound_start_seconds: f64,
    #[serde(default)]
    pub sound_end_seconds: f64,
    #[serde(default)]
    pub audio_output_device_id: String,
}

impl Default for AlertDefinition {
    fn default() -> Self {
        let settings = AlertDefinitionSettings::default();
        Self {
            type_name: String::new(),
            enabled: true,
            text_template: settings.text_template,
            media_path: settings.media_path,
            sound_path: settings.sound_path,
            duration_seconds: settings.duration_seconds,
            priority: settings.priority,
            font_face: settings.font_face,
            font_size: settings.font_size,
            font_color: settings.font_color,
            animation: settings.animation,
            x: settings.x,
            y: settings.y,
            width: settings.width,
            height: settings.height,
            volume_percent: settings.volume_percent,
            sound_start_seconds: settings.sound_start_seconds,
            sound_end_seconds: settings.sound_end_seconds,
            audio_output_device_id: settings.audio_output_device_id,
        }
    }
}

fn default_enabled() -> bool {
    true
}
fn default_duration_seconds() -> i32 {
    8
}
fn default_priority() -> i32 {
    100
}
fn default_font_face() -> String {
    "Segoe UI".into()
}
fn default_font_size() -> i32 {
    44
}
fn default_font_color() -> String {
    "#FFFFFF".into()
}
fn default_animation() -> String {
    "Fade".into()
}
fn default_alert_x() -> i32 {
    510
}
fn default_alert_y() -> i32 {
    690
}
fn default_alert_width() -> i32 {
    900
}
fn default_alert_height() -> i32 {
    260
}
fn default_volume_percent() -> i32 {
    100
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AlertRuntime {
    pub pending_count: usize,
    pub enabled: bool,
    pub obs_scene_name: String,
}

struct QueuedAlert {
    type_name: String,
    user: String,
    duration: Duration,
}

pub struct MemorySettingsStore {
    settings: Mutex<AppSettings>,
    fail_save: AtomicBool,
    save_count: AtomicUsize,
}

impl MemorySettingsStore {
    pub fn new(settings: AppSettings) -> Self {
        Self {
            settings: Mutex::new(settings),
            fail_save: AtomicBool::new(false),
            save_count: AtomicUsize::new(0),
        }
    }

    pub fn fail_saves(&self) {
        self.fail_save.store(true, Ordering::SeqCst);
    }

    pub fn save_count(&self) -> usize {
        self.save_count.load(Ordering::SeqCst)
    }

    pub async fn snapshot(&self) -> AppSettings {
        self.settings.lock().await.clone()
    }

    async fn load(&self) -> Result<AppSettings, String> {
        Ok(self.settings.lock().await.clone())
    }

    async fn save(&self, settings: &AppSettings) -> Result<(), String> {
        self.save_count.fetch_add(1, Ordering::SeqCst);
        if self.fail_save.load(Ordering::SeqCst) {
            return Err("disk full".into());
        }
        *self.settings.lock().await = settings.clone();
        Ok(())
    }
}

#[derive(Clone)]
enum SettingsBackend {
    Json(Arc<JsonSettingsStore>),
    Memory(Arc<MemorySettingsStore>),
}

impl SettingsBackend {
    async fn load(&self) -> Result<AppSettings, String> {
        match self {
            Self::Json(store) => store.load().await.map_err(|e| e.to_string()),
            Self::Memory(store) => store.load().await,
        }
    }

    async fn save(&self, settings: &AppSettings) -> Result<(), String> {
        match self {
            Self::Json(store) => store.save(settings).await.map_err(|e| e.to_string()),
            Self::Memory(store) => store.save(settings).await,
        }
    }
}

struct AlertEngineInner {
    store: SettingsBackend,
    bridge: OverlayEventBridge,
    queue: Mutex<VecDeque<QueuedAlert>>,
    pending: AtomicUsize,
    notify: Notify,
    cancel: AtomicBool,
}

pub struct AlertEngine {
    inner: Arc<AlertEngineInner>,
    worker: std::sync::Mutex<Option<JoinHandle<()>>>,
}

impl AlertEngine {
    pub fn from_store(store: Arc<JsonSettingsStore>, bridge: OverlayEventBridge) -> Self {
        Self::start(SettingsBackend::Json(store), bridge)
    }

    pub fn from_memory(store: Arc<MemorySettingsStore>, bridge: OverlayEventBridge) -> Self {
        Self::start(SettingsBackend::Memory(store), bridge)
    }

    fn start(store: SettingsBackend, bridge: OverlayEventBridge) -> Self {
        Self {
            inner: Arc::new(AlertEngineInner {
                store,
                bridge,
                queue: Mutex::new(VecDeque::new()),
                pending: AtomicUsize::new(0),
                notify: Notify::new(),
                cancel: AtomicBool::new(false),
            }),
            worker: std::sync::Mutex::new(None),
        }
    }

    fn ensure_worker(&self) {
        let mut slot = self.worker.lock().expect("alert worker mutex");
        if slot.is_some() {
            return;
        }
        let inner = self.inner.clone();
        *slot = Some(tokio::spawn(async move { worker_loop(inner).await }));
    }

    pub async fn list(&self) -> Result<Vec<AlertDefinition>, String> {
        let settings = self.inner.store.load().await?;
        let mut rows: Vec<AlertDefinition> = settings
            .alerts
            .definitions
            .iter()
            .map(|(key, def)| to_dto(key, def))
            .collect();
        rows.sort_by(|a, b| a.type_name.to_lowercase().cmp(&b.type_name.to_lowercase()));
        Ok(rows)
    }

    pub async fn upsert(&self, dto: AlertDefinition) -> Result<AlertDefinition, String> {
        let mut settings = self.inner.store.load().await?;
        let requested = dto.type_name.trim().to_string();
        let (key, created) = if requested.is_empty() {
            (
                create_unique_type(&settings.alerts.definitions, "Eigener Alert"),
                true,
            )
        } else if let Some(existing) = find_definition_key(&settings.alerts.definitions, &requested)
        {
            (existing, false)
        } else {
            (requested, true)
        };

        let mut stored = if created {
            let mut def = apply_dto(&dto, &key);
            if def.text_template.trim().is_empty() {
                def.text_template = "{user} hat einen Alert ausgelöst!".into();
            }
            def.enabled = dto.enabled;
            def
        } else {
            apply_dto(&dto, &key)
        };
        stored.r#type = key.clone();
        settings.alerts.definitions.insert(key.clone(), stored.clone());
        self.inner.store.save(&settings).await?;
        Ok(to_dto(&key, &stored))
    }

    pub async fn delete(&self, type_name: &str) -> Result<(), String> {
        let mut settings = self.inner.store.load().await?;
        if settings.alerts.definitions.len() <= 1 {
            return Err("Mindestens ein Alert muss erhalten bleiben.".into());
        }
        let key = find_definition_key(&settings.alerts.definitions, type_name)
            .ok_or_else(|| format!("Alert '{type_name}' wurde nicht gefunden."))?;
        settings.alerts.definitions.remove(&key);
        self.inner.store.save(&settings).await?;
        Ok(())
    }

    pub async fn runtime(&self) -> Result<AlertRuntime, String> {
        let settings = self.inner.store.load().await?;
        Ok(AlertRuntime {
            pending_count: self.pending_count(),
            enabled: settings.alerts.enabled,
            obs_scene_name: settings.alerts.obs_scene_name,
        })
    }

    pub async fn set_runtime(
        &self,
        enabled: Option<bool>,
        obs_scene_name: Option<String>,
    ) -> Result<AlertRuntime, String> {
        if enabled.is_none() && obs_scene_name.is_none() {
            return self.runtime().await;
        }
        let mut settings = self.inner.store.load().await?;
        if let Some(value) = enabled {
            settings.alerts.enabled = value;
        }
        if let Some(name) = obs_scene_name {
            let trimmed = name.trim();
            settings.alerts.obs_scene_name = if trimmed.is_empty() {
                "_alerts".into()
            } else {
                trimmed.to_string()
            };
        }
        self.inner.store.save(&settings).await?;
        self.runtime().await
    }

    pub async fn test_alert(&self, type_name: &str, user: &str) -> Result<usize, String> {
        let settings = self.inner.store.load().await?;
        if !settings.alerts.enabled {
            return Ok(0);
        }
        let key = find_definition_key(&settings.alerts.definitions, type_name)
            .ok_or_else(|| format!("Alert '{type_name}' wurde nicht gefunden."))?;
        let def = settings.alerts.definitions.get(&key).unwrap();
        if !def.enabled {
            return Ok(0);
        }
        self.enqueue_request(to_dto(&key, def), user).await?;
        Ok(1)
    }

    pub async fn enqueue_matching(
        &self,
        event_type: &str,
        data: &BTreeMap<String, String>,
    ) -> usize {
        match self.try_enqueue_matching(event_type, data).await {
            Ok(n) => n,
            Err(_) => 0,
        }
    }

    async fn try_enqueue_matching(
        &self,
        event_type: &str,
        data: &BTreeMap<String, String>,
    ) -> Result<usize, String> {
        let settings = self.inner.store.load().await?;
        if !settings.alerts.enabled {
            return Ok(0);
        }
        let mapped = alert_type_for_event(event_type);
        let matched = settings.alerts.definitions.iter().find(|(key, def)| {
            def.enabled && definition_matches(key, def, event_type, mapped)
        });
        let Some((key, def)) = matched else {
            return Ok(0);
        };
        let user = user_from_event(data);
        self.enqueue_request(to_dto(key, def), &user).await?;
        Ok(1)
    }

    async fn enqueue_request(&self, def: AlertDefinition, user: &str) -> Result<(), String> {
        self.ensure_worker();
        let settings = self.inner.store.load().await?;
        let cap = settings.alerts.queue_capacity.max(1) as usize;
        let duration = Duration::from_secs(def.duration_seconds.max(0) as u64);
        let mut queue = self.inner.queue.lock().await;
        while queue.len() >= cap {
            queue.pop_front();
            self.inner.pending.fetch_sub(1, Ordering::SeqCst);
        }
        queue.push_back(QueuedAlert {
            type_name: def.type_name,
            user: user.to_string(),
            duration,
        });
        self.inner.pending.fetch_add(1, Ordering::SeqCst);
        drop(queue);
        self.inner.notify.notify_one();
        Ok(())
    }

    pub fn pending_count(&self) -> usize {
        self.inner.pending.load(Ordering::SeqCst)
    }
}

impl Drop for AlertEngine {
    fn drop(&mut self) {
        self.inner.cancel.store(true, Ordering::SeqCst);
        self.inner.notify.notify_waiters();
        if let Ok(mut worker) = self.worker.lock() {
            if let Some(handle) = worker.take() {
                handle.abort();
            }
        }
    }
}

async fn worker_loop(inner: Arc<AlertEngineInner>) {
    loop {
        if inner.cancel.load(Ordering::SeqCst) {
            break;
        }
        let notified = inner.notify.notified();
        let next = inner.queue.lock().await.pop_front();
        match next {
            Some(alert) => {
                drop(notified);
                inner.bridge.app_alert(&alert.type_name, &alert.user);
                if !alert.duration.is_zero() {
                    tokio::time::sleep(alert.duration).await;
                }
                inner.pending.fetch_sub(1, Ordering::SeqCst);
            }
            None => notified.await,
        }
    }
}

fn to_dto(key: &str, def: &AlertDefinitionSettings) -> AlertDefinition {
    AlertDefinition {
        type_name: def.effective_type(key),
        enabled: def.enabled,
        text_template: def.text_template.clone(),
        media_path: def.media_path.clone(),
        sound_path: def.sound_path.clone(),
        duration_seconds: def.duration_seconds,
        priority: def.priority,
        font_face: def.font_face.clone(),
        font_size: def.font_size,
        font_color: def.font_color.clone(),
        animation: def.animation.clone(),
        x: def.x,
        y: def.y,
        width: def.width,
        height: def.height,
        volume_percent: def.volume_percent,
        sound_start_seconds: def.sound_start_seconds,
        sound_end_seconds: def.sound_end_seconds,
        audio_output_device_id: def.audio_output_device_id.clone(),
    }
}

fn apply_dto(dto: &AlertDefinition, key: &str) -> AlertDefinitionSettings {
    AlertDefinitionSettings {
        r#type: key.to_string(),
        enabled: dto.enabled,
        text_template: dto.text_template.clone(),
        media_path: dto.media_path.clone(),
        sound_path: dto.sound_path.clone(),
        duration_seconds: dto.duration_seconds,
        priority: dto.priority,
        font_face: dto.font_face.clone(),
        font_size: dto.font_size,
        font_color: dto.font_color.clone(),
        animation: dto.animation.clone(),
        x: dto.x,
        y: dto.y,
        width: dto.width,
        height: dto.height,
        volume_percent: dto.volume_percent,
        sound_start_seconds: dto.sound_start_seconds,
        sound_end_seconds: dto.sound_end_seconds,
        audio_output_device_id: dto.audio_output_device_id.clone(),
    }
}

fn find_definition_key(
    defs: &HashMap<String, AlertDefinitionSettings>,
    type_name: &str,
) -> Option<String> {
    defs.iter().find_map(|(key, def)| {
        if key.eq_ignore_ascii_case(type_name) || def.r#type.eq_ignore_ascii_case(type_name) {
            Some(key.clone())
        } else {
            None
        }
    })
}

fn create_unique_type(
    defs: &HashMap<String, AlertDefinitionSettings>,
    base_type: &str,
) -> String {
    let cleaned = if base_type.trim().is_empty() {
        "Eigener Alert".to_string()
    } else {
        base_type.trim().to_string()
    };
    if find_definition_key(defs, &cleaned).is_none() {
        return cleaned;
    }
    for suffix in 2..1000 {
        let candidate = format!("{cleaned} {suffix}");
        if find_definition_key(defs, &candidate).is_none() {
            return candidate;
        }
    }
    format!(
        "{cleaned} {}",
        &uuid::Uuid::new_v4().simple().to_string()[..6]
    )
}

fn definition_matches(
    key: &str,
    def: &AlertDefinitionSettings,
    event_type: &str,
    mapped: Option<&str>,
) -> bool {
    let ty = def.effective_type(key);
    ty.eq_ignore_ascii_case(event_type)
        || key.eq_ignore_ascii_case(event_type)
        || mapped.is_some_and(|mapped| {
            ty.eq_ignore_ascii_case(mapped) || key.eq_ignore_ascii_case(mapped)
        })
}

fn user_from_event(data: &BTreeMap<String, String>) -> String {
    data.get("user_name")
        .filter(|v| !v.trim().is_empty())
        .cloned()
        .or_else(|| {
            data.get("from_broadcaster_user_name")
                .filter(|v| !v.trim().is_empty())
                .cloned()
        })
        .unwrap_or_else(|| "Twitch".into())
}

#[cfg(test)]
mod tests {
    use super::*;
    use ccs_overlay_server::RealtimeHub;
    use serde_json::Value;

    fn engine_with(
        settings: AppSettings,
    ) -> (
        AlertEngine,
        Arc<MemorySettingsStore>,
        tokio::sync::broadcast::Receiver<String>,
    ) {
        let store = Arc::new(MemorySettingsStore::new(settings));
        let hub = Arc::new(RealtimeHub::new());
        let rx = hub.subscribe();
        let engine = AlertEngine::from_memory(store.clone(), OverlayEventBridge::new(hub));
        (engine, store, rx)
    }

    fn default_settings() -> AppSettings {
        AppSettings::default()
    }

    async fn wait_until(mut cond: impl FnMut() -> bool) {
        let deadline = tokio::time::Instant::now() + Duration::from_secs(2);
        while tokio::time::Instant::now() < deadline {
            if cond() {
                return;
            }
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
        assert!(cond(), "Bedingung innerhalb des Timeouts nicht erfüllt.");
    }

    #[tokio::test]
    async fn create_uses_unique_type_and_persists() {
        let mut settings = default_settings();
        settings.alerts.definitions.insert(
            "Eigener Alert".into(),
            AlertDefinitionSettings {
                r#type: "Eigener Alert".into(),
                ..Default::default()
            },
        );
        let (engine, store, _rx) = engine_with(settings);
        let created = engine.upsert(AlertDefinition::default()).await.unwrap();
        assert_eq!(created.type_name, "Eigener Alert 2");
        assert_eq!(
            created.text_template,
            "{user} hat einen Alert ausgelöst!"
        );
        assert_eq!(store.save_count(), 1);
        assert!(store
            .snapshot()
            .await
            .alerts
            .definitions
            .contains_key("Eigener Alert 2"));
    }

    #[tokio::test]
    async fn duplicate_copies_definition_fields() {
        let mut settings = default_settings();
        settings.alerts.definitions.insert(
            "Source".into(),
            AlertDefinitionSettings {
                r#type: "Source".into(),
                enabled: false,
                text_template: "Text".into(),
                media_path: "media.mp4".into(),
                sound_path: "sound.wav".into(),
                duration_seconds: 14,
                priority: 12,
                font_face: "Inter".into(),
                font_size: 51,
                font_color: "#123456".into(),
                animation: "Zoom".into(),
                volume_percent: 65,
                sound_start_seconds: 1.25,
                sound_end_seconds: 4.5,
                audio_output_device_id: "device".into(),
                ..Default::default()
            },
        );
        let (engine, _store, _rx) = engine_with(settings);
        let source = engine
            .list()
            .await
            .unwrap()
            .into_iter()
            .find(|d| d.type_name == "Source")
            .unwrap();
        let mut duplicate = source;
        duplicate.type_name = "Source Kopie".into();
        let saved = engine.upsert(duplicate).await.unwrap();
        assert_eq!(saved.type_name, "Source Kopie");
        assert!(!saved.enabled);
        assert_eq!(saved.text_template, "Text");
        assert_eq!(saved.media_path, "media.mp4");
        assert_eq!(saved.sound_path, "sound.wav");
        assert_eq!(saved.duration_seconds, 14);
        assert_eq!(saved.priority, 12);
        assert_eq!(saved.font_face, "Inter");
        assert_eq!(saved.font_size, 51);
        assert_eq!(saved.font_color, "#123456");
        assert_eq!(saved.animation, "Zoom");
        assert_eq!(saved.volume_percent, 65);
        assert_eq!(saved.sound_start_seconds, 1.25);
        assert_eq!(saved.sound_end_seconds, 4.5);
        assert_eq!(saved.audio_output_device_id, "device");
    }

    #[tokio::test]
    async fn toggle_rolls_back_when_persistence_fails() {
        let settings = default_settings();
        let initial = settings.alerts.definitions["Follow"].enabled;
        let (engine, store, _rx) = engine_with(settings);
        store.fail_saves();
        let mut follow = engine
            .list()
            .await
            .unwrap()
            .into_iter()
            .find(|d| d.type_name == "Follow")
            .unwrap();
        follow.enabled = !initial;
        let err = engine.upsert(follow).await.unwrap_err();
        assert!(err.contains("disk full"));
        assert_eq!(
            store.snapshot().await.alerts.definitions["Follow"].enabled,
            initial
        );
    }

    #[tokio::test]
    async fn delete_rolls_back_when_persistence_fails() {
        let (engine, store, _rx) = engine_with(default_settings());
        store.fail_saves();
        let err = engine.delete("Follow").await.unwrap_err();
        assert!(err.contains("disk full"));
        assert!(store
            .snapshot()
            .await
            .alerts
            .definitions
            .contains_key("Follow"));
    }

    #[tokio::test]
    async fn delete_rejects_last_definition() {
        let mut settings = default_settings();
        settings.alerts.definitions = HashMap::from([(
            "Only".into(),
            AlertDefinitionSettings {
                r#type: "Only".into(),
                ..Default::default()
            },
        )]);
        let (engine, store, _rx) = engine_with(settings);
        let err = engine.delete("Only").await.unwrap_err();
        assert!(err.contains("Mindestens"));
        assert_eq!(store.save_count(), 0);
        assert!(store
            .snapshot()
            .await
            .alerts
            .definitions
            .contains_key("Only"));
    }

    #[tokio::test]
    async fn json_store_roundtrip_survives_engine_restart() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("settings.json");
        let store = Arc::new(JsonSettingsStore::new(&path));
        let mut settings = AppSettings::default();
        settings
            .alerts
            .definitions
            .get_mut("Follow")
            .unwrap()
            .duration_seconds = 0;
        store.save(&settings).await.unwrap();

        let hub = Arc::new(RealtimeHub::new());
        let engine = AlertEngine::from_store(store.clone(), OverlayEventBridge::new(hub));
        let created = engine
            .upsert(AlertDefinition {
                type_name: String::new(),
                ..Default::default()
            })
            .await
            .unwrap();
        assert_eq!(created.type_name, "Eigener Alert");
        drop(engine);

        let reloaded = AlertEngine::from_store(
            store,
            OverlayEventBridge::new(Arc::new(RealtimeHub::new())),
        );
        let types: Vec<_> = reloaded
            .list()
            .await
            .unwrap()
            .into_iter()
            .map(|d| d.type_name)
            .collect();
        assert!(types.iter().any(|t| t == "Eigener Alert"));
        assert!(types.iter().any(|t| t == "Follow"));
    }

    #[tokio::test]
    async fn enqueue_matching_publishes_app_alert() {
        let mut settings = default_settings();
        settings.alerts.definitions.get_mut("Follow").unwrap().duration_seconds = 0;
        settings.alerts.definitions.get_mut("Cheer").unwrap().enabled = false;
        settings.alerts.definitions.get_mut("Cheer").unwrap().duration_seconds = 0;
        let (engine, _store, mut rx) = engine_with(settings);

        let mut data = BTreeMap::new();
        data.insert("user_name".into(), "alice".into());
        let n = engine.enqueue_matching("channel.follow", &data).await;
        assert_eq!(n, 1);

        let frame = tokio::time::timeout(Duration::from_secs(1), rx.recv())
            .await
            .expect("hub timeout")
            .expect("hub closed");
        let root: Value = serde_json::from_str(&frame).unwrap();
        assert_eq!(root["source"], "app");
        assert_eq!(root["type"], "app.alert");
        assert_eq!(root["data"]["alertType"], "Follow");
        assert_eq!(root["data"]["user"], "alice");

        wait_until(|| engine.pending_count() == 0).await;
        let skipped = engine
            .enqueue_matching("channel.cheer", &BTreeMap::new())
            .await;
        assert_eq!(skipped, 0);
    }

    #[tokio::test]
    async fn enqueue_matching_skips_when_runtime_disabled() {
        let mut settings = default_settings();
        settings.alerts.enabled = false;
        settings.alerts.definitions.get_mut("Follow").unwrap().duration_seconds = 0;
        let (engine, _store, mut rx) = engine_with(settings);
        let n = engine
            .enqueue_matching("channel.follow", &BTreeMap::new())
            .await;
        assert_eq!(n, 0);
        assert!(rx.try_recv().is_err());
    }

    #[tokio::test]
    async fn test_alert_enqueues_overlay_payload() {
        let mut settings = default_settings();
        settings.alerts.definitions.get_mut("Follow").unwrap().duration_seconds = 0;
        let (engine, _store, mut rx) = engine_with(settings);
        assert_eq!(engine.test_alert("Follow", "Tester").await.unwrap(), 1);
        let frame = tokio::time::timeout(Duration::from_secs(1), rx.recv())
            .await
            .expect("hub timeout")
            .expect("hub closed");
        let root: Value = serde_json::from_str(&frame).unwrap();
        assert_eq!(root["data"]["alertType"], "Follow");
        assert_eq!(root["data"]["user"], "Tester");
    }

    #[tokio::test]
    async fn set_runtime_persists_obs_scene_name() {
        let (engine, store, _rx) = engine_with(default_settings());
        let runtime = engine
            .set_runtime(Some(false), Some(" overlay ".into()))
            .await
            .unwrap();
        assert!(!runtime.enabled);
        assert_eq!(runtime.obs_scene_name, "overlay");
        let snap = store.snapshot().await;
        assert!(!snap.alerts.enabled);
        assert_eq!(snap.alerts.obs_scene_name, "overlay");
    }
}
