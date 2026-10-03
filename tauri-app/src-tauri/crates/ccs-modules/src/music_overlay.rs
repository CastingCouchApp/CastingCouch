use crate::{
    music_player::MusicPlayerSnapshot,
    obs::{ObsClient, ObsControl, ObsQuery},
    ConnectionState, ServiceStatus,
};
use ccs_core::{AppSettings, JsonSettingsStore};
use serde_json::{json, Value};
use std::{
    sync::{Arc, Mutex as StdMutex},
    time::{Duration, Instant},
};
use tokio::sync::{broadcast, Mutex};

struct OverlayConfig {
    enabled: bool,
    automatic: bool,
    hide_paused: bool,
    hide_muted: bool,
    detect_volume: bool,
    detect_obs: bool,
    audio_source: String,
    scene: String,
    source: String,
}
impl OverlayConfig {
    fn from_settings(settings: &AppSettings) -> Self {
        let extra = &settings.spotify.extra;
        let flag = |key: &str, fallback: bool| extra[key].as_bool().unwrap_or(fallback);
        let name =
            |key: &str, fallback: &str| extra[key].as_str().unwrap_or(fallback).trim().to_string();
        Self {
            enabled: flag("OverlayEnabled", true),
            automatic: flag("SmartAutomationEnabled", true),
            hide_paused: flag("OverlayHideWhenPaused", false),
            hide_muted: flag("OverlayHideWhenMuted", true),
            detect_volume: flag("OverlayMuteDetectionSpotifyVolume", true),
            detect_obs: flag("OverlayMuteDetectionObsSource", true),
            audio_source: name("OverlayObsAudioSource", "Spotify"),
            scene: name("OverlayObsScene", ""),
            source: name("OverlayObsSource", "ccs_spotify"),
        }
    }
}

#[derive(Default)]
struct VisibilityState {
    music: Option<MusicPlayerSnapshot>,
    provider: String,
    last_playing: Option<Instant>,
    obs_mute: Option<(String, bool)>,
    error: Option<String>,
}
impl VisibilityState {
    fn payload(
        &mut self,
        config: &OverlayConfig,
        music: &MusicPlayerSnapshot,
        now: Instant,
    ) -> Value {
        if self.provider != music.provider || !music.connected {
            self.last_playing = None;
            self.provider = music.provider.clone();
        }
        if music.connected && music.is_playing {
            self.last_playing = Some(now);
        }
        self.music = Some(music.clone());
        let obs_mute = self
            .obs_mute
            .as_ref()
            .filter(|(name, _)| name == &config.audio_source)
            .map(|(_, value)| *value);
        let paused = config.hide_paused
            && !music.is_playing
            && self
                .last_playing
                .is_none_or(|at| now.saturating_duration_since(at) >= Duration::from_secs(3));
        let volume = config.hide_muted && config.detect_volume && music.volume_percent == Some(0);
        let obs = config.hide_muted && config.detect_obs && obs_mute == Some(true);
        let hidden = music.connected && config.automatic && (paused || volume || obs);
        let visible = music.connected && !hidden;
        let mut data = serde_json::to_value(music).expect("serializable music snapshot");
        for (key, value) in [
            ("cover", json!(music.cover_url)),
            ("visible", json!(visible)),
            ("showInOverlay", json!(visible)),
            ("showTitle", json!(true)),
            ("showArtist", json!(true)),
            ("showAlbumCover", json!(true)),
            ("showProgress", json!(true)),
            ("hideWhenPaused", json!(config.hide_paused)),
            ("hideWhenMuted", json!(config.hide_muted)),
            ("muteDetectionObsSource", json!(config.detect_obs)),
            ("muteDetectionSpotifyVolume", json!(config.detect_volume)),
            ("obsAudioSource", json!(config.audio_source)),
            ("obsAudioMuted", json!(obs_mute)),
            ("obsSourceVisible", json!(!hidden)),
            ("overlayEnabled", json!(config.enabled)),
            ("overlayError", json!(self.error)),
        ] {
            data[key] = value;
        }
        data
    }
}
struct ObsSyncState {
    applied: Option<(String, String, bool)>,
    events: broadcast::Receiver<ServiceStatus>,
}
pub struct MusicOverlayRuntime {
    settings: Arc<JsonSettingsStore>,
    obs: Arc<ObsClient>,
    state: StdMutex<VisibilityState>,
    sync: Mutex<ObsSyncState>,
}
impl MusicOverlayRuntime {
    pub fn new(settings: Arc<JsonSettingsStore>, obs: Arc<ObsClient>) -> Self {
        let events = obs.subscribe_status();
        Self {
            settings,
            obs,
            state: StdMutex::new(VisibilityState::default()),
            sync: Mutex::new(ObsSyncState {
                applied: None,
                events,
            }),
        }
    }
    /// No network operations: JSON visibility remains current while OBS is slow or unavailable.
    pub fn snapshot(&self, music: &MusicPlayerSnapshot, settings: &AppSettings) -> Value {
        self.state.lock().unwrap().payload(
            &OverlayConfig::from_settings(settings),
            music,
            Instant::now(),
        )
    }
    pub async fn tick(&self) -> Result<(), String> {
        let mut sync = self.sync.lock().await;
        loop {
            match sync.events.try_recv() {
                Ok(_) | Err(broadcast::error::TryRecvError::Lagged(_)) => sync.applied = None,
                Err(_) => break,
            }
        }
        if self.obs.status().await.state != ConnectionState::Connected {
            sync.applied = None;
            self.state.lock().unwrap().error = None;
            return Ok(());
        }
        let settings = self.settings.load().await.map_err(|e| e.to_string())?;
        let config = OverlayConfig::from_settings(&settings);
        let mut errors = vec![];
        if config.automatic
            && config.hide_muted
            && config.detect_obs
            && !config.audio_source.is_empty()
        {
            match self
                .obs
                .query(ObsQuery::Mute {
                    input_name: config.audio_source.clone(),
                })
                .await
            {
                Ok(value) => {
                    if let Some(muted) = value["inputMuted"].as_bool() {
                        self.state.lock().unwrap().obs_mute =
                            Some((config.audio_source.clone(), muted));
                    } else {
                        errors.push("OBS-Mute-Antwort enthält keinen gültigen Status.".to_string());
                    }
                }
                Err(error) => errors.push(error.to_string()),
            }
        }
        // Re-read after network operations. A changed configuration must not switch an old source.
        let latest = self.settings.load().await.map_err(|e| e.to_string())?;
        let target = OverlayConfig::from_settings(&latest);
        if !target.scene.is_empty() && !target.source.is_empty() {
            let wanted = {
                let mut state = self.state.lock().unwrap();
                let music = state.music.clone().unwrap_or_default();
                state.payload(&target, &music, Instant::now())["obsSourceVisible"] == true
            };
            let applied = (target.scene.clone(), target.source.clone(), wanted);
            if sync.applied.as_ref() != Some(&applied) {
                let result = async {
                    let item = self
                        .obs
                        .send_request(
                            "GetSceneItemId",
                            Some(json!({"sceneName":target.scene,"sourceName":target.source})),
                        )
                        .await
                        .map_err(|e| e.to_string())?;
                    let id = item["sceneItemId"].as_i64().ok_or("OBS-Quellen-ID fehlt")?;
                    let current = OverlayConfig::from_settings(
                        &self.settings.load().await.map_err(|e| e.to_string())?,
                    );
                    if current.scene != target.scene || current.source != target.source {
                        return Ok(None);
                    }
                    let visible = {
                        let mut state = self.state.lock().unwrap();
                        let music = state.music.clone().unwrap_or_default();
                        state.payload(&current, &music, Instant::now())["obsSourceVisible"] == true
                    };
                    self.obs
                        .control(ObsControl::SetVisibility {
                            scene_name: current.scene.clone(),
                            scene_item_id: id,
                            scene_item_enabled: visible,
                        })
                        .await
                        .map_err(|e| e.to_string())?;
                    Ok::<_, String>(Some((current.scene, current.source, visible)))
                }
                .await;
                match result {
                    Ok(applied) => sync.applied = applied,
                    Err(error) => {
                        sync.applied = None;
                        errors.push(error);
                    }
                }
            }
        } else {
            sync.applied = None;
        }
        let error = if errors.is_empty() {
            None
        } else {
            Some(errors.join(" · "))
        };
        self.state.lock().unwrap().error = error.clone();
        error.map_or(Ok(()), Err)
    }
}

#[cfg(test)]
mod tests;
