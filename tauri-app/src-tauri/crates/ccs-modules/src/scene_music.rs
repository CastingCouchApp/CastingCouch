//! Native scene/stream music automation. All volume writes share alert ducking.
use crate::{
    music_automation::AlertDucking,
    spotify::{SpotifyAction, SpotifyClient, SpotifyQuery},
    ModuleError, ModuleResult,
};
use ccs_core::store::JsonSettingsStore;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{
    sync::{
        atomic::{AtomicBool, AtomicUsize, Ordering},
        Arc, Mutex as StdMutex,
    },
    time::Duration,
};
use tokio::sync::{broadcast, watch, Mutex};

#[derive(Clone, Serialize)]
pub struct MusicAutomationStatus {
    pub running: bool,
    pub action: String,
    pub history: Vec<MusicLog>,
}
#[derive(Clone, Serialize)]
pub struct MusicLog {
    pub at: chrono::DateTime<chrono::Utc>,
    pub rule: String,
    pub success: bool,
    pub message: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(
    tag = "action",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum MusicAction {
    Scene {
        scene: String,
        force: bool,
    },
    StreamStarted,
    StreamStopped,
    StartPlaylist,
    FadeIn,
    FadeOut,
    FadeTo {
        percent: u8,
        milliseconds: u64,
        pause_at_end: bool,
    },
    Stop,
    RestoreState {
        state: Value,
        fade_seconds: u32,
    },
}

#[derive(Debug, Deserialize)]
#[serde(default, rename_all = "PascalCase")]
struct Rule {
    name: String,
    enabled: bool,
    trigger_type: String,
    trigger_value: String,
    action_type: String,
    playlist_uri: String,
    shuffle: bool,
    volume_percent: i64,
    fade_enabled: bool,
    fade_milliseconds: i64,
    delay_seconds: i64,
}
impl Default for Rule {
    fn default() -> Self {
        Self {
            name: "Spotify-Regel".into(),
            enabled: true,
            trigger_type: "ObsSceneChanged".into(),
            trigger_value: String::new(),
            action_type: "Resume".into(),
            playlist_uri: String::new(),
            shuffle: true,
            volume_percent: 75,
            fade_enabled: false,
            fade_milliseconds: 500,
            delay_seconds: 0,
        }
    }
}
#[derive(Default)]
struct Runtime {
    generation: u64,
    running: bool,
    action: String,
    history: Vec<MusicLog>,
}
pub struct SceneMusicEngine {
    settings: Arc<JsonSettingsStore>,
    player: Arc<SpotifyClient>,
    ducking: Arc<AlertDucking>,
    generation: watch::Sender<u64>,
    ticket_lock: StdMutex<()>,
    gate: Mutex<()>,
    runtime: Mutex<Runtime>,
    stream: StdMutex<Option<bool>>,
    status_tx: broadcast::Sender<MusicAutomationStatus>,
    closed: AtomicBool,
    stream_end_owners: AtomicUsize,
    managed_stop: AtomicBool,
    managed_scene: StdMutex<Option<String>>,
}
pub struct StreamEndMusicLease {
    engine: Arc<SceneMusicEngine>,
    retain_stop: AtomicBool,
}
impl StreamEndMusicLease {
    pub fn mark_stop_started(&self) {
        self.engine.managed_stop.store(true, Ordering::SeqCst);
    }
    pub fn mark_stop_completed(&self) {
        self.retain_stop.store(true, Ordering::SeqCst);
    }
}
impl Drop for StreamEndMusicLease {
    fn drop(&mut self) {
        if self.engine.stream_end_owners.fetch_sub(1, Ordering::SeqCst) == 1
            && !self.retain_stop.load(Ordering::SeqCst)
        {
            self.engine.managed_stop.store(false, Ordering::SeqCst);
        }
    }
}
impl SceneMusicEngine {
    pub fn new(
        settings: Arc<JsonSettingsStore>,
        player: Arc<SpotifyClient>,
        ducking: Arc<AlertDucking>,
    ) -> Self {
        Self {
            settings,
            player,
            ducking,
            generation: watch::channel(0).0,
            ticket_lock: StdMutex::new(()),
            gate: Mutex::new(()),
            runtime: Mutex::new(Runtime::default()),
            stream: StdMutex::new(None),
            status_tx: broadcast::channel(32).0,
            closed: AtomicBool::new(false),
            stream_end_owners: AtomicUsize::new(0),
            managed_stop: AtomicBool::new(false),
            managed_scene: StdMutex::new(None),
        }
    }
    pub fn cancel(&self) {
        let _guard = self.ticket_lock.lock().unwrap();
        self.generation.send_modify(|n| *n = n.wrapping_add(1));
    }
    pub fn claim_stream_end(self: &Arc<Self>) -> StreamEndMusicLease {
        self.stream_end_owners.fetch_add(1, Ordering::SeqCst);
        self.cancel();
        StreamEndMusicLease {
            engine: self.clone(),
            retain_stop: AtomicBool::new(false),
        }
    }
    /// Prevent a delayed OBS notification from executing an already managed scene twice.
    /// The next distinct scene releases this marker and resumes normal scene rules.
    pub fn manage_scene(&self, scene: &str) {
        *self.managed_scene.lock().unwrap() = Some(scene.into());
    }
    pub fn release_managed_scene(&self, scene: &str) {
        let mut managed = self.managed_scene.lock().unwrap();
        if managed
            .as_ref()
            .is_some_and(|s| s.eq_ignore_ascii_case(scene))
        {
            *managed = None;
        }
    }
    pub async fn shutdown(&self) {
        self.cancel();
        let _idle = self.gate.lock().await;
    }
    pub async fn manual_player_guard(&self) -> tokio::sync::MutexGuard<'_, ()> {
        self.cancel();
        self.gate.lock().await
    }
    pub async fn close(&self) {
        self.closed.store(true, Ordering::SeqCst);
        self.shutdown().await;
    }
    pub fn is_closed(&self) -> bool {
        self.closed.load(Ordering::SeqCst)
    }
    /// Maintenance never cancels a user action and rechecks playback after acquiring the gate.
    pub async fn recover_missing_device(
        &self,
        client: &str,
        options: &Value,
    ) -> ModuleResult<Option<Value>> {
        let Ok(_idle) = self.gate.try_lock() else {
            return Ok(None);
        };
        if self.is_closed() || self.ducking.desired_volume().await.is_some() {
            return Ok(None);
        }
        let mut ticket = self.generation.subscribe();
        tokio::select! {
            biased;
            _ = ticket.changed() => Err(cancelled()),
            result = async {
                let current = self.player.query(client, SpotifyQuery::Playback, None).await?;
                if !current["device"].is_null() || self.is_closed() { return Ok(None); }
                self.player.activate_preferred_device(client, options, false).await.map(Some)
            } => result,
        }
    }
    pub async fn status(&self) -> Value {
        json!(self.snapshot().await)
    }
    async fn snapshot(&self) -> MusicAutomationStatus {
        let state = self.runtime.lock().await;
        MusicAutomationStatus {
            running: state.running,
            action: state.action.clone(),
            history: state.history.clone(),
        }
    }
    pub fn subscribe_status(&self) -> broadcast::Receiver<MusicAutomationStatus> {
        self.status_tx.subscribe()
    }
    async fn publish_status(&self) {
        let _ = self.status_tx.send(self.snapshot().await);
    }
    pub fn bind_obs(
        self: &Arc<Self>,
        obs: &crate::obs::ObsClient,
    ) -> impl std::future::Future<Output = ()> + Send + 'static {
        let mut scenes = obs.subscribe_scenes();
        let engine = self.clone();
        async move {
            let mut previous = String::new();
            loop {
                match scenes.recv().await {
                    Ok(scene) => {
                        if engine.closed.load(Ordering::SeqCst) {
                            break;
                        }
                        if previous.eq_ignore_ascii_case(&scene) {
                            continue;
                        }
                        previous = scene.clone();
                        let managed = {
                            let mut owned = engine.managed_scene.lock().unwrap();
                            if owned
                                .as_ref()
                                .is_some_and(|s| s.eq_ignore_ascii_case(&scene))
                            {
                                true
                            } else {
                                *owned = None;
                                false
                            }
                        };
                        if managed {
                            continue;
                        }
                        engine.dispatch(MusicAction::Scene {
                            scene,
                            force: false,
                        });
                    }
                    Err(tokio::sync::broadcast::error::RecvError::Lagged(_)) => continue,
                    Err(tokio::sync::broadcast::error::RecvError::Closed) => break,
                }
            }
        }
    }
    pub fn dispatch(
        self: &Arc<Self>,
        action: MusicAction,
    ) -> tokio::task::JoinHandle<ModuleResult<Value>> {
        let ticket = self.ticket();
        let engine = self.clone();
        tokio::spawn(async move { engine.execute(action, ticket).await })
    }
    pub async fn run(&self, action: MusicAction) -> ModuleResult<Value> {
        if matches!(action, MusicAction::Stop) {
            self.shutdown().await;
            return Ok(self.status().await);
        }
        self.execute(action, self.ticket()).await
    }
    fn ticket(&self) -> watch::Receiver<u64> {
        let _guard = self.ticket_lock.lock().unwrap();
        self.generation.send_modify(|n| *n = n.wrapping_add(1));
        self.generation.subscribe()
    }
    /// Connection gaps do not reset the observed session or retrigger its music.
    pub async fn observe_stream(
        self: &Arc<Self>,
        active: Option<bool>,
    ) -> Option<tokio::task::JoinHandle<ModuleResult<Value>>> {
        if self.closed.load(Ordering::SeqCst) {
            return None;
        }
        let active = active?;
        let ended = {
            let mut previous = self.stream.lock().unwrap();
            if *previous == Some(active) {
                return None;
            }
            let ended = *previous == Some(true) && !active;
            *previous = Some(active);
            ended
        };
        if active {
            self.managed_stop.store(false, Ordering::SeqCst);
            if self.stream_end_owners.load(Ordering::SeqCst) > 0 {
                return None;
            }
        } else if ended && self.managed_stop.swap(false, Ordering::SeqCst) {
            return None;
        }
        let settings = match self.settings.read_value().await {
            Ok(value) => value,
            Err(error) => {
                self.log("Streambeobachtung", false, &error.to_string())
                    .await;
                return None;
            }
        };
        let typed: ccs_core::AppSettings = serde_json::from_value(settings.clone()).ok()?;
        if typed.music_player.provider_id() != "spotify" {
            return None;
        }
        if active
            && !preference(
                &settings,
                "StartOnStreamStart",
                "AutoStartSpotifyPlaylist",
                true,
            )
        {
            return None;
        }
        if ended
            && !preference(
                &settings,
                "PauseOnStreamEnd",
                "PauseSpotifyOnStreamEnd",
                true,
            )
        {
            return None;
        }
        if active {
            Some(self.dispatch(MusicAction::StreamStarted))
        } else if ended {
            Some(self.dispatch(MusicAction::StreamStopped))
        } else {
            None
        }
    }
    async fn execute(
        &self,
        action: MusicAction,
        mut ticket: watch::Receiver<u64>,
    ) -> ModuleResult<Value> {
        if self.closed.load(Ordering::SeqCst) {
            return Err(ModuleError::Message("Musikautomatik wurde beendet".into()));
        }
        let generation = *ticket.borrow();
        let guard = tokio::select! { biased; _ = ticket.changed() => return Err(cancelled()), guard = self.gate.lock() => guard };
        if *self.generation.borrow() != generation {
            return Err(cancelled());
        }
        let label = match &action {
            MusicAction::Scene { scene, .. } => format!("Szene: {scene}"),
            _ => format!("{action:?}"),
        };
        {
            let mut state = self.runtime.lock().await;
            state.generation = generation;
            state.running = true;
            state.action = label.clone();
        }
        self.publish_status().await;
        let result = tokio::select! { biased; _ = ticket.changed() => Err(cancelled()), result = self.perform(action) => result };
        if let Err(error) = &result {
            self.log(&label, false, &error.to_string()).await;
        }
        {
            let mut state = self.runtime.lock().await;
            if state.generation == generation {
                state.running = false;
            }
        }
        self.publish_status().await;
        drop(guard);
        result.map(|()| json!({"success":true}))
    }
    async fn log(&self, rule: &str, success: bool, message: &str) {
        let mut state = self.runtime.lock().await;
        state.history.insert(
            0,
            MusicLog {
                at: chrono::Utc::now(),
                rule: rule.into(),
                success,
                message: message.into(),
            },
        );
        state.history.truncate(50);
        drop(state);
        self.publish_status().await;
    }
    async fn perform(&self, action: MusicAction) -> ModuleResult<()> {
        let settings = self
            .settings
            .read_value()
            .await
            .map_err(|e| ModuleError::Message(e.to_string()))?;
        let options = &settings["Spotify"];
        let client = options["ClientId"].as_str().unwrap_or("");
        let automatic = matches!(
            action,
            MusicAction::Scene { force: false, .. }
                | MusicAction::StreamStarted
                | MusicAction::StreamStopped
        );
        let typed: ccs_core::settings::AppSettings = serde_json::from_value(settings.clone())
            .map_err(|e| ModuleError::Message(e.to_string()))?;
        if automatic && typed.music_player.provider_id() != "spotify" {
            return Ok(());
        }
        match action {
            MusicAction::Scene { scene, force } => {
                if scene.trim().is_empty() {
                    return Ok(());
                }
                let rules: Vec<Rule> = if force || flag(options, "SmartAutomationEnabled", true) {
                    serde_json::from_value(
                        options["AutomationRules"]
                            .as_array()
                            .cloned()
                            .map(Value::Array)
                            .unwrap_or(json!([])),
                    )
                    .map_err(|e| {
                        ModuleError::Message(format!("Spotify-Regeln sind ungültig: {e}"))
                    })?
                } else {
                    vec![]
                };
                let matching: Vec<_> = rules
                    .iter()
                    .filter(|r| {
                        r.enabled
                            && r.trigger_type.eq_ignore_ascii_case("ObsSceneChanged")
                            && r.trigger_value.eq_ignore_ascii_case(scene.trim())
                    })
                    .collect();
                let mut errors = vec![];
                if scene.eq_ignore_ascii_case(settings["Obs"]["EndScene"].as_str().unwrap_or(""))
                    && preference(&settings, "PlayEndMusic", "AutoPlayEndMusic", false)
                    && !matching
                        .iter()
                        .any(|r| r.action_type.eq_ignore_ascii_case("StartPlaylist"))
                {
                    if let Err(error) = self.start_configured(client, options).await {
                        errors.push(error.to_string());
                    }
                }
                for rule in matching {
                    if rule.delay_seconds > 0 {
                        tokio::time::sleep(
                            Duration::from_secs(rule.delay_seconds.min(3600) as u64),
                        )
                        .await;
                    }
                    match self
                        .apply_rule(client, options, &settings["Obs"], &scene, rule)
                        .await
                    {
                        Ok(()) => self.log(&rule.name, true, "Musikregel ausgeführt").await,
                        Err(error) => {
                            self.log(&rule.name, false, &error.to_string()).await;
                            errors.push(error.to_string());
                        }
                    }
                }
                if !errors.is_empty() {
                    return Err(ModuleError::Message(errors.join("; ")));
                }
            }
            MusicAction::StreamStarted => {
                if !preference(
                    &settings,
                    "StartOnStreamStart",
                    "AutoStartSpotifyPlaylist",
                    true,
                ) {
                    return Ok(());
                }
                if let Err(first) = self.start_configured(client, options).await {
                    self.log("Streamstart", false, &format!("Erneuter Versuch: {first}"))
                        .await;
                    tokio::time::sleep(Duration::from_secs(2)).await;
                    self.start_configured(client, options).await?;
                }
                self.log("Streamstart", true, "Startplaylist gestartet")
                    .await;
            }
            MusicAction::StreamStopped => {
                if !preference(
                    &settings,
                    "PauseOnStreamEnd",
                    "PauseSpotifyOnStreamEnd",
                    true,
                ) {
                    return Ok(());
                }
                if flag(options, "FadeOutEnabled", true) {
                    self.fade(
                        client,
                        options,
                        0,
                        seconds(options, "FadeOutSeconds", 3),
                        true,
                    )
                    .await?;
                } else {
                    self.player
                        .action_with_preferences(client, SpotifyAction::Pause, options)
                        .await?;
                }
                self.log("Streamende", true, "Musik pausiert").await;
            }
            MusicAction::StartPlaylist => self.start_configured(client, options).await?,
            MusicAction::FadeIn => {
                self.fade(
                    client,
                    options,
                    percent(options, "StartVolumePercent", 100),
                    seconds(options, "FadeInSeconds", 3),
                    false,
                )
                .await?
            }
            MusicAction::FadeOut => {
                self.fade(
                    client,
                    options,
                    0,
                    seconds(options, "FadeOutSeconds", 3),
                    flag(options, "PauseAfterFadeOut", true),
                )
                .await?
            }
            MusicAction::FadeTo {
                percent,
                milliseconds,
                pause_at_end,
            } => {
                if percent > 100 || milliseconds > 60000 {
                    return Err(ModuleError::Message(
                        "Fade: Lautstärke 0–100, Dauer 0–60000 ms".into(),
                    ));
                }
                self.fade(client, options, percent, milliseconds, pause_at_end)
                    .await?;
            }
            MusicAction::Stop => (),
            MusicAction::RestoreState {
                state,
                fade_seconds,
            } => {
                if fade_seconds > 300 {
                    return Err(ModuleError::Message(
                        "Wiederherstellungs-Fade darf höchstens 300 Sekunden dauern".into(),
                    ));
                }
                let state: ccs_core::spotify_states::SavedPlaybackState =
                    serde_json::from_value(state)
                        .map_err(|e| ModuleError::Message(e.to_string()))?;
                state
                    .validate()
                    .map_err(|e| ModuleError::Message(e.to_string()))?;
                let action = SpotifyAction::RestorePlayback {
                    context_uri: state.context_uri.clone(),
                    track_uri: state.track["Uri"].as_str().map(str::to_string),
                };
                action.validate()?;
                let device = self
                    .player
                    .activate_preferred_device(client, options, false)
                    .await?["id"]
                    .as_str()
                    .map(str::to_string);
                if fade_seconds > 0 {
                    self.ducking
                        .set_volume_on_device(client, 0, device.as_deref())
                        .await?;
                }
                self.player
                    .action_on_device(
                        client,
                        SpotifyAction::Repeat {
                            mode: state.repeat_mode.clone(),
                        },
                        device.as_deref(),
                    )
                    .await?;
                self.player
                    .action_on_device(client, action, device.as_deref())
                    .await?;
                self.player
                    .action_on_device(
                        client,
                        SpotifyAction::Shuffle {
                            enabled: state.shuffle_enabled,
                        },
                        device.as_deref(),
                    )
                    .await?;
                if state.progress_ms > 0 {
                    tokio::time::sleep(Duration::from_millis(350)).await;
                    self.player
                        .action_on_device(
                            client,
                            SpotifyAction::Seek {
                                position_ms: state.progress_ms,
                            },
                            device.as_deref(),
                        )
                        .await?;
                }
                if fade_seconds == 0 {
                    self.ducking
                        .set_volume_on_device(client, state.volume_percent, device.as_deref())
                        .await?;
                } else {
                    let steps = (fade_seconds as u64 * 4).min(120);
                    for step in 1..=steps {
                        let volume = (state.volume_percent as f64 * step as f64 / steps as f64)
                            .round() as u8;
                        self.ducking
                            .set_volume_on_device(client, volume, device.as_deref())
                            .await?;
                        if step < steps {
                            tokio::time::sleep(Duration::from_millis(
                                fade_seconds as u64 * 1000 / steps,
                            ))
                            .await;
                        }
                    }
                }
                if !state.was_playing {
                    self.player
                        .action_on_device(client, SpotifyAction::Pause, device.as_deref())
                        .await?;
                }
                self.log("Zustand", true, "Wiedergabe wiederhergestellt")
                    .await;
            }
        }
        Ok(())
    }
    async fn apply_rule(
        &self,
        client: &str,
        options: &Value,
        obs: &Value,
        scene: &str,
        rule: &Rule,
    ) -> ModuleResult<()> {
        let live = obs["LiveScene"]
            .as_str()
            .filter(|s| !s.trim().is_empty())
            .unwrap_or("Game");
        if scene.eq_ignore_ascii_case(live)
            && rule.action_type.eq_ignore_ascii_case("Pause")
            && flag(options, "SetVolumeOnLiveTransition", true)
            && !flag(options, "MuteOnLiveTransition", false)
        {
            self.ducking
                .set_volume_on_device(
                    client,
                    percent(options, "LiveVolumePercent", 75),
                    preferred(options),
                )
                .await?;
            return Ok(());
        }
        let target = rule.volume_percent.clamp(0, 100) as u8;
        let action = rule.action_type.to_ascii_lowercase();
        let mut selected = None;
        match action.as_str() {
            "startplaylist" => {
                selected = self
                    .start(client, options, &rule.playlist_uri, rule.shuffle, None)
                    .await?
            }
            "pause" => {
                self.player
                    .action_with_preferences(client, SpotifyAction::Pause, options)
                    .await?;
            }
            "setvolume" => {
                self.ducking
                    .set_volume_on_device(client, target, preferred(options))
                    .await?;
            }
            _ => {
                self.player
                    .action_with_preferences(client, SpotifyAction::Play, options)
                    .await?;
            }
        }
        if action == "startplaylist" || action == "resume" {
            let mut options = options.clone();
            if let Some(id) = selected {
                options["PreferredDeviceId"] = json!(id);
            }
            if rule.fade_enabled && rule.fade_milliseconds > 0 {
                self.fade(
                    client,
                    &options,
                    target,
                    rule.fade_milliseconds.clamp(0, 60000) as u64,
                    false,
                )
                .await?;
            } else {
                self.ducking
                    .set_volume_on_device(client, target, preferred(&options))
                    .await?;
            }
        }
        Ok(())
    }
    async fn start(
        &self,
        client: &str,
        options: &Value,
        uri: &str,
        shuffle: bool,
        initial_volume: Option<u8>,
    ) -> ModuleResult<Option<String>> {
        let uri = uri.trim();
        if !uri
            .strip_prefix("spotify:playlist:")
            .is_some_and(|id| !id.is_empty() && id.bytes().all(|b| b.is_ascii_alphanumeric()))
        {
            return Err(ModuleError::Message(
                "Ungültige Spotify-Startplaylist".into(),
            ));
        }
        let device = if flag(options, "AutoTransferToPreferredDevice", true) {
            self.player
                .activate_preferred_device(client, options, false)
                .await?["id"]
                .as_str()
                .map(str::to_string)
        } else {
            preferred(options).map(str::to_string)
        };
        self.player
            .action_on_device(
                client,
                SpotifyAction::PlayPlaylist { uri: uri.into() },
                device.as_deref(),
            )
            .await?;
        if let Some(volume) = initial_volume {
            self.ducking
                .set_volume_on_device(client, volume, device.as_deref())
                .await?;
        }
        self.player
            .action_on_device(
                client,
                SpotifyAction::Shuffle { enabled: shuffle },
                device.as_deref(),
            )
            .await?;
        Ok(device)
    }
    async fn start_configured(&self, client: &str, options: &Value) -> ModuleResult<()> {
        let target = percent(options, "StartVolumePercent", 100);
        let fade = flag(options, "FadeInEnabled", true);
        let device = self
            .start(
                client,
                options,
                options["StartPlaylistUri"].as_str().unwrap_or(""),
                flag(options, "ShuffleSelectedPlaylist", false),
                Some(if fade { 0 } else { target }),
            )
            .await?;
        let mut options = options.clone();
        if let Some(id) = device {
            options["PreferredDeviceId"] = json!(id);
        }
        if fade {
            self.fade_from(
                client,
                &options,
                target,
                seconds(&options, "FadeInSeconds", 3),
                false,
                Some(0),
            )
            .await?;
        }
        Ok(())
    }
    async fn fade(
        &self,
        client: &str,
        options: &Value,
        target: u8,
        milliseconds: u64,
        pause: bool,
    ) -> ModuleResult<()> {
        self.fade_from(client, options, target, milliseconds, pause, None)
            .await
    }
    async fn fade_from(
        &self,
        client: &str,
        options: &Value,
        target: u8,
        milliseconds: u64,
        pause: bool,
        start: Option<u8>,
    ) -> ModuleResult<()> {
        let playback = self
            .player
            .query(client, SpotifyQuery::Playback, None)
            .await?;
        let desired = self.ducking.desired_volume().await;
        let device = desired
            .as_ref()
            .map(|(id, _)| id.as_str())
            .or(preferred(options))
            .or(playback["device"]["id"].as_str())
            .filter(|id| !id.is_empty())
            .ok_or_else(|| {
                ModuleError::Message("Kein Spotify-Gerät für den Fade erreichbar".into())
            })?
            .to_string();
        let volume = if let Some(v) = start {
            v
        } else if let Some((_, v)) = desired {
            v
        } else if playback["device"]["id"] == device {
            valid_volume(&playback["device"]["volume_percent"])?
        } else {
            let devices = self
                .player
                .query(client, SpotifyQuery::Devices, None)
                .await?;
            let selected = devices["devices"]
                .as_array()
                .and_then(|rows| rows.iter().find(|d| d["id"] == device))
                .ok_or_else(|| {
                    ModuleError::Message("Spotify-Fadegerät ist nicht erreichbar".into())
                })?;
            valid_volume(&selected["volume_percent"])?
        };
        let steps = if milliseconds == 0 {
            1
        } else {
            milliseconds.div_ceil(150)
        };
        for step in 1..=steps {
            let value = (volume as f64
                + (target as f64 - volume as f64) * step as f64 / steps as f64)
                .round()
                .clamp(0.0, 100.0) as u8;
            self.ducking
                .set_volume_on_device(client, value, Some(&device))
                .await?;
            if step < steps {
                tokio::time::sleep(Duration::from_millis(milliseconds / steps)).await;
            }
        }
        if pause && target == 0 {
            self.player
                .action_on_device(client, SpotifyAction::Pause, Some(&device))
                .await?;
        }
        Ok(())
    }
}
fn cancelled() -> ModuleError {
    ModuleError::Message("Musikaktion abgebrochen".into())
}
fn flag(v: &Value, key: &str, default: bool) -> bool {
    v[key].as_bool().unwrap_or(default)
}
fn preference(settings: &Value, key: &str, legacy: &str, default: bool) -> bool {
    settings["Spotify"][key]
        .as_bool()
        .or(settings["Workflow"][legacy].as_bool())
        .unwrap_or(default)
}
fn percent(v: &Value, key: &str, default: i64) -> u8 {
    v[key].as_i64().unwrap_or(default).clamp(0, 100) as u8
}
fn seconds(v: &Value, key: &str, default: u64) -> u64 {
    v[key].as_u64().unwrap_or(default).min(60) * 1000
}
fn preferred(v: &Value) -> Option<&str> {
    v["PreferredDeviceId"]
        .as_str()
        .filter(|s| !s.trim().is_empty())
}
fn valid_volume(v: &Value) -> ModuleResult<u8> {
    v.as_u64()
        .filter(|n| *n <= 100)
        .map(|n| n as u8)
        .ok_or_else(|| {
            ModuleError::Message("Spotify-Gerät meldet keine regelbare Lautstärke".into())
        })
}
