use super::*;
use ccs_modules::{
    music_player::MusicPlayerAction,
    scene_music::{MusicAction, SceneMusicEngine, StreamEndMusicLease},
    stream_end::{
        IoFuture, RaidIdentity, StreamEndIo, StreamEndOperation, StreamEndPlan,
        StreamEndPreferences, StreamEndReply,
    },
};
use std::sync::atomic::{AtomicBool, Ordering};

pub(super) async fn snapshot(state: &AppState) -> Result<Value, String> {
    let original = state
        .settings
        .read_value()
        .await
        .map_err(|e| e.to_string())?;
    let draft = StreamEndPreferences::read(&original)?;
    Ok(
        json!({"draft":draft,"original":original,"outgoingRaid":state.twitch.outgoing_raid_subscription().await,
        "raidChannels":original["Twitch"].get("RaidChannels").cloned().unwrap_or_else(||json!([])),
        "endScene":original["Obs"]["EndScene"].as_str().unwrap_or(""),"warnings":[]}),
    )
}
pub(super) async fn start(
    state: &AppState,
    planned: bool,
    seconds: Option<u32>,
    reviewed: Option<Value>,
) -> Result<ccs_modules::stream_end::StreamEndSnapshot, String> {
    let _gate = state.stream_end_gate.lock().await;
    let _settings = state.settings_mutation.lock().await;
    if state.stream_end.snapshot().await.active {
        return Err("Ein Streamende läuft bereits".into());
    }
    let original = state
        .settings
        .read_value()
        .await
        .map_err(|e| e.to_string())?;
    let settings: AppSettings =
        serde_json::from_value(original.clone()).map_err(|e| e.to_string())?;
    let mut preferences = StreamEndPreferences::read(&original)?;
    if let Some(reviewed) = reviewed {
        if StreamEndPreferences::read(&reviewed)? != preferences
            || reviewed["Obs"]["EndScene"] != original["Obs"]["EndScene"]
            || reviewed["Obs"]["StartScene"] != original["Obs"]["StartScene"]
        {
            return Err(
                "Streamende-Auswahl wurde inzwischen geändert; Einstellungen neu laden".into(),
            );
        }
    }
    let delay = if planned {
        preferences.mode = preferences.planned_mode().into();
        seconds
            .or_else(|| (preferences.planned_seconds > 0).then_some(preferences.planned_seconds))
            .or_else(|| preferences.planned_minutes.checked_mul(60))
            .ok_or("Geplantes Streamende zu lang")?
    } else {
        if seconds.is_some() {
            return Err("Dauer nur bei geplantem Streamende angeben".into());
        }
        0
    };
    if planned && delay == 0 {
        return Err("Geplantes Streamende benötigt mindestens eine Sekunde".into());
    }
    if state.obs.status().await.state != ccs_modules::ConnectionState::Connected {
        return Err("OBS nicht verbunden".into());
    }
    let outputs = state.obs.output_status().await.map_err(|e| e.to_string())?;
    if outputs
        .pointer("/stream/outputActive")
        .and_then(Value::as_bool)
        != Some(true)
    {
        return Err("OBS-Stream läuft nicht oder der Ausgangsstatus ist unbekannt".into());
    }
    let wants_raid = preferences.mode == "EndSceneRaidThenStop"
        && !preferences.selected_raid_channel.trim().is_empty();
    let (broadcaster_id, broadcaster_login) = if wants_raid {
        if !settings.twitch.enable_event_sub {
            return Err("Ausgehende Raid-Bestätigung benötigt aktiviertes Twitch EventSub".into());
        }
        let identity = state
            .twitch
            .raid_broadcaster(&settings.twitch.client_id, &settings.twitch.channel_name)
            .await
            .map_err(|e| e.to_string())?;
        let subscription = state.twitch.outgoing_raid_subscription().await;
        if !subscription.available || subscription.broadcaster_id != identity.0 {
            return Err(format!(
                "Ausgehende Raid-Bestätigung nicht verfügbar: {}",
                subscription
                    .error
                    .unwrap_or_else(|| "EventSub gehört zu einem anderen Kanal".into())
            ));
        }
        let pending = state
            .twitch
            .community_raid_state(&settings.twitch.client_id, &settings.twitch.channel_name)
            .await;
        if pending.requested_target.is_some() {
            return Err("Vorherigen Raid zuerst in Twitch prüfen oder abbrechen".into());
        }
        identity
    } else {
        (String::new(), String::new())
    };
    let end_scene = settings.obs.end_scene.clone();
    let plan = StreamEndPlan {
        preferences,
        end_scene: end_scene.clone(),
        start_scene: settings.obs.start_scene.clone(),
        broadcaster_id: broadcaster_id.clone(),
        broadcaster_login,
        // Manage the full existing scene policy once, including custom playlist precedence.
        play_end_music: !end_scene.trim().is_empty()
            || music_preference(&original, "PlayEndMusic", "AutoPlayEndMusic", false),
        pause_music_on_stream_end: music_preference(
            &original,
            "PauseOnStreamEnd",
            "PauseSpotifyOnStreamEnd",
            true,
        ),
    };
    let io = Arc::new(NativeStreamEndIo {
        settings,
        secrets: state.secrets.clone(),
        obs: state.obs.clone(),
        twitch: state.twitch.clone(),
        music: state.music_player.clone(),
        scene_music: state.scene_music.clone(),
        lease: Mutex::new(None),
        stopped: AtomicBool::new(false),
        broadcaster_id,
    });
    state.stream_end.start(plan, delay, io).await
}
fn music_preference(original: &Value, native: &str, legacy: &str, default: bool) -> bool {
    original["Spotify"]
        .get(native)
        .and_then(Value::as_bool)
        .or_else(|| original["Workflow"].get(legacy).and_then(Value::as_bool))
        .unwrap_or(default)
}
pub(super) async fn reject_active(state: &AppState) -> Result<(), String> {
    if state.stream_end.snapshot().await.active {
        Err(
            "Raid wird vom Streamende-Assistenten gesteuert; dort abbrechen oder überspringen"
                .into(),
        )
    } else {
        Ok(())
    }
}
pub(super) async fn reject_new_raid(state: &AppState) -> Result<(), String> {
    reject_active(state).await?;
    if state.stream_end.snapshot().await.raid_pending {
        Err("Vorherigen Raid zuerst in Twitch prüfen oder abbrechen".into())
    } else {
        Ok(())
    }
}
pub(super) async fn validate_resolution_channel(state: &AppState) -> Result<(), String> {
    let pending = state.stream_end.snapshot().await;
    if pending.raid_pending && !pending.broadcaster_id.is_empty() {
        let settings = state.settings.load().await.map_err(|e| e.to_string())?;
        let (id, _) = state
            .twitch
            .raid_broadcaster(&settings.twitch.client_id, &settings.twitch.channel_name)
            .await
            .map_err(|e| e.to_string())?;
        if id != pending.broadcaster_id {
            return Err(format!(
                "Unaufgelöster Raid gehört zu {}; vor dem Abbruch diesen Twitch-Kanal verbinden",
                pending.broadcaster_login
            ));
        }
    }
    Ok(())
}
struct NativeStreamEndIo {
    settings: AppSettings,
    secrets: Arc<KeyringSecretStore>,
    obs: Arc<ObsClient>,
    twitch: Arc<TwitchClient>,
    music: Arc<ccs_modules::music_player::MusicPlayerRuntime>,
    scene_music: Arc<SceneMusicEngine>,
    lease: Mutex<Option<Arc<StreamEndMusicLease>>>,
    stopped: AtomicBool,
    broadcaster_id: String,
}
impl NativeStreamEndIo {
    async fn lease(&self) -> Arc<StreamEndMusicLease> {
        let mut lease = self.lease.lock().await;
        lease
            .get_or_insert_with(|| Arc::new(self.scene_music.claim_stream_end()))
            .clone()
    }
    async fn connect_obs(&self) -> Result<(), String> {
        if self.obs.status().await.state == ccs_modules::ConnectionState::Connected {
            return Ok(());
        }
        let password = self
            .secrets
            .get(OBS_PASSWORD_SECRET_KEY)
            .map_err(|e| e.to_string())?
            .filter(|s| !s.is_empty());
        self.obs
            .connect(ObsConnectOptions {
                host: self.settings.obs.host.clone(),
                port: self.settings.obs.port,
                password,
                reconnect: self.settings.general.connection_watchdog_enabled
                    && self.settings.general.reconnect_obs,
                reconnect_seconds: self.settings.general.connection_watchdog_seconds.max(1) as u64,
            })
            .await
            .map_err(|e| e.to_string())
    }
}
impl StreamEndIo for NativeStreamEndIo {
    fn execute(&self, operation: StreamEndOperation) -> IoFuture<'_> {
        Box::pin(async move {
            let twitch = &self.settings.twitch;
            match operation {
                StreamEndOperation::SetScene { scene } => {
                    self.lease().await;
                    self.scene_music.manage_scene(&scene);
                    if let Err(error) = self.obs.set_current_program_scene(&scene).await {
                        self.scene_music.release_managed_scene(&scene);
                        return Err(error.to_string());
                    }
                    if self.stopped.load(Ordering::SeqCst) {
                        if let Err(error) = self
                            .scene_music
                            .run(MusicAction::Scene {
                                scene,
                                force: false,
                            })
                            .await
                        {
                            return Ok(StreamEndReply::Warnings(vec![format!(
                                "Startszene gesetzt; Szenenmusik fehlgeschlagen: {error}"
                            )]));
                        }
                    }
                }
                StreamEndOperation::PlayEndMusic => {
                    self.lease().await;
                    if !self.settings.obs.end_scene.trim().is_empty() {
                        self.scene_music
                            .run(MusicAction::Scene {
                                scene: self.settings.obs.end_scene.clone(),
                                force: false,
                            })
                            .await
                            .map_err(|e| e.to_string())?;
                    } else if self.settings.music_player.provider_id() == "spotify" {
                        self.scene_music
                            .run(MusicAction::StartPlaylist)
                            .await
                            .map_err(|e| e.to_string())?;
                    }
                }
                StreamEndOperation::PauseMusic => {
                    self.music
                        .action(MusicPlayerAction::Pause)
                        .await
                        .map_err(|e| e.to_string())?;
                }
                StreamEndOperation::StopStream => {
                    let lease = self.lease().await;
                    self.connect_obs().await?;
                    let outputs = self.obs.output_status().await.map_err(|e| e.to_string())?;
                    let active = outputs
                        .pointer("/stream/outputActive")
                        .and_then(Value::as_bool)
                        .ok_or("OBS-Streamstatus unbekannt")?;
                    lease.mark_stop_started();
                    if active {
                        self.obs
                            .control(ObsControl::StopStream)
                            .await
                            .map_err(|e| e.to_string())?;
                    }
                    lease.mark_stop_completed();
                    self.stopped.store(true, Ordering::SeqCst);
                }
                StreamEndOperation::ProbeRaid { login } => {
                    let target = self
                        .twitch
                        .community_target(&twitch.client_id, &twitch.channel_name, &login)
                        .await
                        .map_err(|e| e.to_string())?;
                    return Ok(StreamEndReply::Target(target.map(|target| RaidIdentity {
                        id: target.id,
                        login: target.login,
                        display_name: target.display_name,
                        online: target.is_online,
                    })));
                }
                StreamEndOperation::StartRaid { login } => {
                    let subscription = self.twitch.outgoing_raid_subscription().await;
                    if !subscription.available || subscription.broadcaster_id != self.broadcaster_id
                    {
                        return Err(
                            "Twitch EventSub für diesen Kanal nicht verbunden; kein Raid gesendet"
                                .into(),
                        );
                    }
                    let result = self
                        .twitch
                        .community_start(&twitch.client_id, &twitch.channel_name, &login, false)
                        .await
                        .map_err(|e| e.to_string())?;
                    if !result.warnings.is_empty() {
                        return Ok(StreamEndReply::Warnings(result.warnings));
                    }
                }
                StreamEndOperation::CancelRaid => {
                    self.twitch
                        .community_cancel(&twitch.client_id, &twitch.channel_name)
                        .await
                        .map_err(|e| e.to_string())?;
                }
                StreamEndOperation::RaidNow { login } => {
                    if !twitch.enable_chat {
                        return Err("Twitch-Chat ist deaktiviert; Raid bei Twitch ausführen".into());
                    }
                    self.twitch
                        .action(
                            &twitch.client_id,
                            &twitch.channel_name,
                            ccs_modules::twitch::TwitchAction::SendChat {
                                message: format!(
                                    "/raid {}",
                                    ccs_modules::twitch::checked_raid_login(&login)
                                        .map_err(|e| e.to_string())?
                                ),
                            },
                        )
                        .await
                        .map_err(|e| e.to_string())?;
                }
                StreamEndOperation::AcknowledgeRaid => {
                    self.twitch.acknowledge_raid().await;
                }
            }
            Ok(StreamEndReply::Done)
        })
    }
}
pub(super) fn spawn_events<R: tauri::Runtime>(
    app: AppHandle<R>,
    runtime: Arc<ccs_modules::stream_end::StreamEndRuntime>,
) {
    let mut events = runtime.subscribe_changes();
    tauri::async_runtime::spawn(async move {
        loop {
            match events.recv().await {
                Ok(snapshot) => {
                    let _ = app.emit("stream-end-changed", &snapshot);
                    if let Some(state) = app.try_state::<AppState>() {
                        state
                            .hub
                            .live
                            .merge_snapshot(&json!({"streamEnd":snapshot}));
                        let _ = app.emit("twitch-raids-changed", json!({"changed":true}));
                    }
                }
                Err(broadcast::error::RecvError::Lagged(_)) => {
                    let _ = app.emit("stream-end-changed", runtime.snapshot().await);
                }
                Err(broadcast::error::RecvError::Closed) => break,
            }
        }
    });
}
