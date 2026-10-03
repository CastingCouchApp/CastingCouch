use crate::{
    music_automation::AlertDucking,
    scene_music::SceneMusicEngine,
    spotify::{SpotifyAction, SpotifyClient, SpotifyQuery},
    ConnectionState, ModuleError, ModuleResult,
};
use ccs_core::{AppSettings, JsonSettingsStore};
use ccs_overlay_server::YouTubeMusicBridge;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::sync::{
    atomic::{AtomicU64, Ordering},
    Arc,
};
use tokio::sync::Mutex;

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(
    tag = "action",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum MusicPlayerAction {
    Play,
    Pause,
    PlayPause,
    Next,
    Previous,
    Seek { position_ms: u32 },
    Volume { percent: u8 },
}

#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MusicPlayerSnapshot {
    pub provider: String,
    pub provider_display_name: String,
    pub connected: bool,
    pub connecting: bool,
    pub bridge_running: bool,
    pub is_playing: bool,
    pub title: String,
    pub artist: String,
    pub album: String,
    pub cover_url: String,
    pub progress_ms: i64,
    pub duration_ms: i64,
    pub volume_percent: Option<u8>,
    pub supports_seek: bool,
    pub supports_volume: bool,
    pub status_text: String,
    pub error: Option<String>,
}

#[derive(Default)]
struct PlaybackDetails {
    volume: Option<u8>,
    requested: Option<(u8, std::time::Instant)>,
    error: Option<String>,
}
impl PlaybackDetails {
    fn effective_volume(&self, now: std::time::Instant) -> Option<u8> {
        self.requested
            .filter(|(_, at)| {
                now.saturating_duration_since(*at) < std::time::Duration::from_secs(4)
            })
            .map(|(value, _)| value)
            .or(self.volume)
    }
}

/// One provider, one command route and one cached snapshot for the app and overlays.
/// Network refreshes never hold the command gate, so a slow API cannot block provider changes.
pub struct MusicPlayerRuntime {
    settings: Arc<JsonSettingsStore>,
    spotify: Arc<SpotifyClient>,
    scene: Arc<SceneMusicEngine>,
    ducking: Arc<AlertDucking>,
    ytm: Arc<Mutex<Option<Arc<YouTubeMusicBridge>>>>,
    gate: Mutex<Option<String>>,
    details: Mutex<PlaybackDetails>,
    last_playing: Mutex<Option<crate::spotify::NowPlaying>>,
    revision: AtomicU64,
}
impl MusicPlayerRuntime {
    pub fn new(
        settings: Arc<JsonSettingsStore>,
        spotify: Arc<SpotifyClient>,
        scene: Arc<SceneMusicEngine>,
        ducking: Arc<AlertDucking>,
        ytm: Arc<Mutex<Option<Arc<YouTubeMusicBridge>>>>,
    ) -> Self {
        Self {
            settings,
            spotify,
            scene,
            ducking,
            ytm,
            gate: Mutex::new(None),
            details: Mutex::new(PlaybackDetails::default()),
            last_playing: Mutex::new(None),
            revision: AtomicU64::new(0),
        }
    }
    async fn settings(&self) -> ModuleResult<AppSettings> {
        self.settings
            .load()
            .await
            .map_err(|e| ModuleError::Message(e.to_string()))
    }
    async fn clear_details(&self) {
        // Advance while holding the same cache lock checked by refresh_details.
        let mut details = self.details.lock().await;
        self.revision.fetch_add(1, Ordering::SeqCst);
        *details = PlaybackDetails::default();
        *self.last_playing.lock().await = None;
    }
    async fn synchronize(
        &self,
        active: &mut Option<String>,
        settings: &AppSettings,
    ) -> ModuleResult<()> {
        let provider = settings.music_player.provider_id();
        if active.as_deref() != Some(provider) {
            self.scene.shutdown().await;
            self.clear_details().await;
            *active = Some(provider.into());
        }
        if provider == "ytmusic" {
            if self.spotify.status().await.state != ConnectionState::Disconnected {
                self.spotify.disconnect().await?;
            }
        } else if let Some(bridge) = self.ytm.lock().await.take() {
            bridge.stop_and_wait().await;
        }
        Ok(())
    }
    pub async fn apply_provider(&self) -> ModuleResult<()> {
        let mut active = self.gate.lock().await;
        self.synchronize(&mut active, &self.settings().await?).await
    }
    /// Host connection operations keep this guard until their connection is established.
    pub async fn provider_guard(
        &self,
        expected: &str,
    ) -> ModuleResult<tokio::sync::MutexGuard<'_, Option<String>>> {
        let mut active = self.gate.lock().await;
        let settings = self.settings().await?;
        if settings.music_player.provider_id() != expected {
            return Err(ModuleError::Message(format!(
                "Bitte zuerst den Musikprovider {expected} auswählen."
            )));
        }
        self.synchronize(&mut active, &settings).await?;
        Ok(active)
    }
    pub async fn disconnect(&self) -> ModuleResult<()> {
        let mut active = self.gate.lock().await;
        let settings = self.settings().await?;
        self.synchronize(&mut active, &settings).await?;
        self.scene.shutdown().await;
        if settings.music_player.provider_id() == "ytmusic" {
            if let Some(bridge) = self.ytm.lock().await.take() {
                bridge.stop_and_wait().await;
            }
        } else {
            self.spotify.disconnect().await?;
        }
        self.clear_details().await;
        Ok(())
    }
    pub async fn action(&self, action: MusicPlayerAction) -> ModuleResult<Value> {
        self.action_from(None, action).await
    }
    pub async fn action_for_provider(
        &self,
        provider: &str,
        action: MusicPlayerAction,
    ) -> ModuleResult<Value> {
        self.action_from(Some(provider), action).await
    }
    async fn action_from(
        &self,
        expected: Option<&str>,
        action: MusicPlayerAction,
    ) -> ModuleResult<Value> {
        let mut active = self.gate.lock().await;
        let settings = self.settings().await?;
        if let Some(provider) =
            expected.filter(|provider| *provider != settings.music_player.provider_id())
        {
            return Err(ModuleError::Message(format!(
                "Bitte zuerst den Musikprovider {provider} auswählen."
            )));
        }
        self.synchronize(&mut active, &settings).await?;
        if settings.music_player.provider_id() == "ytmusic" {
            let command = match action {
                MusicPlayerAction::Play => "play",
                MusicPlayerAction::Pause => "pause",
                MusicPlayerAction::PlayPause => "playpause",
                MusicPlayerAction::Next => "next",
                MusicPlayerAction::Previous => "previous",
                _ => {
                    return Err(ModuleError::Message(
                        "YouTube Music unterstützt hier weder Seek noch Lautstärke.".into(),
                    ))
                }
            };
            let current = self.ytm.lock().await;
            let bridge = current.as_ref().ok_or_else(|| {
                ModuleError::Message("YouTube-Music-Bridge ist nicht verbunden.".into())
            })?;
            bridge.command(command).map_err(ModuleError::Message)?;
            return Ok(Value::Null);
        }
        // A manual command also cancels a pending fade when the connection has failed.
        let _player = self.scene.manual_player_guard().await;
        if !self.snapshot().await?.connected {
            return Err(ModuleError::Message("Spotify ist nicht verbunden.".into()));
        }
        let spotify_action = match action {
            MusicPlayerAction::Play => SpotifyAction::Play,
            MusicPlayerAction::Pause => SpotifyAction::Pause,
            MusicPlayerAction::PlayPause => {
                if self.spotify.now_playing().await.is_playing {
                    SpotifyAction::Pause
                } else {
                    SpotifyAction::Play
                }
            }
            MusicPlayerAction::Next => SpotifyAction::Next,
            MusicPlayerAction::Previous => SpotifyAction::Previous,
            MusicPlayerAction::Seek { position_ms } => SpotifyAction::Seek { position_ms },
            MusicPlayerAction::Volume { percent } => {
                let result = self
                    .ducking
                    .set_volume_on_device(
                        &settings.spotify.client_id,
                        percent,
                        settings.spotify.extra["PreferredDeviceId"].as_str(),
                    )
                    .await?;
                let mut details = self.details.lock().await;
                self.revision.fetch_add(1, Ordering::SeqCst);
                details.volume = Some(percent);
                details.requested = Some((percent, std::time::Instant::now()));
                details.error = None;
                return Ok(result);
            }
        };
        self.spotify
            .action_with_preferences(
                &settings.spotify.client_id,
                spotify_action,
                &settings.spotify.extra,
            )
            .await
    }
    pub async fn refresh_details(&self) {
        let revision = self.revision.load(Ordering::SeqCst);
        let Ok(settings) = self.settings().await else {
            return;
        };
        if settings.music_player.provider_id() != "spotify"
            || self.spotify.status().await.state != ConnectionState::Connected
        {
            return;
        }
        let result = tokio::time::timeout(
            std::time::Duration::from_secs(3),
            self.spotify
                .query(&settings.spotify.client_id, SpotifyQuery::Playback, None),
        )
        .await;
        let mut details = self.details.lock().await;
        if revision != self.revision.load(Ordering::SeqCst) {
            return;
        }
        match result {
            Ok(Ok(playback)) => {
                details.volume = playback["device"]["volume_percent"]
                    .as_u64()
                    .filter(|n| *n <= 100)
                    .map(|n| n as u8);
                details.error = None;
            }
            Ok(Err(error)) => details.error = Some(error.to_string()),
            Err(_) => details.error = Some("Spotify-Wiedergabestatus: Zeitüberschreitung".into()),
        }
    }
    pub async fn snapshot(&self) -> ModuleResult<MusicPlayerSnapshot> {
        let settings = self.settings().await?;
        if settings.music_player.provider_id() == "ytmusic" {
            let current = self.ytm.lock().await;
            if let Some(bridge) = current.as_ref() {
                bridge.set_timeout_seconds(settings.you_tube_music.timeout_seconds());
                let s = bridge.snapshot();
                return Ok(MusicPlayerSnapshot {
                    provider: "ytmusic".into(),
                    provider_display_name: "YouTube Music".into(),
                    connected: s.connected,
                    bridge_running: s.bridge_running,
                    is_playing: s.is_playing,
                    title: s.title,
                    artist: s.artist,
                    album: s.album,
                    cover_url: s.cover_url,
                    progress_ms: s.progress_ms,
                    duration_ms: s.duration_ms,
                    status_text: s.status_text,
                    error: bridge.error(),
                    ..Default::default()
                });
            }
            return Ok(MusicPlayerSnapshot {
                provider: "ytmusic".into(),
                provider_display_name: "YouTube Music".into(),
                status_text: "Bridge gestoppt".into(),
                ..Default::default()
            });
        }
        let status = self.spotify.status().await;
        let current = self.spotify.now_playing().await;
        let (connected, playing) = {
            let mut last = self.last_playing.lock().await;
            if status.state == ConnectionState::Disconnected
                || status.state == ConnectionState::Connecting
            {
                *last = None;
            }
            let connected = status.state == ConnectionState::Connected
                || (status.state == ConnectionState::Error && last.is_some());
            if connected && (!current.title.is_empty() || !current.track_id.is_empty()) {
                *last = Some(current.clone());
            }
            (
                connected,
                if connected {
                    last.clone().unwrap_or(current)
                } else {
                    Default::default()
                },
            )
        };
        let connecting = status.state == ConnectionState::Connecting;
        let details = self.details.lock().await;
        Ok(MusicPlayerSnapshot {
            provider: "spotify".into(),
            provider_display_name: "Spotify".into(),
            connected,
            connecting,
            is_playing: playing.is_playing,
            title: playing.title.clone(),
            artist: playing.artist,
            album: playing.album,
            cover_url: playing.cover_url,
            progress_ms: playing.progress_ms.max(0),
            duration_ms: playing.duration_ms.max(0),
            volume_percent: if connected {
                details.effective_volume(std::time::Instant::now())
            } else {
                None
            },
            supports_seek: true,
            supports_volume: true,
            status_text: if connecting {
                "Verbinde …"
            } else if !connected {
                "Nicht verbunden"
            } else if playing.title.is_empty() {
                "Verbunden · Kein Titel"
            } else if playing.is_playing {
                "Spielt"
            } else {
                "Pause"
            }
            .into(),
            error: if status.state == ConnectionState::Error {
                Some(status.detail)
            } else if connected {
                details.error.clone()
            } else {
                None
            },
            ..Default::default()
        })
    }
}

#[cfg(test)]
mod tests;
