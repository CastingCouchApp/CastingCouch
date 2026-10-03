use crate::{
    music_automation::AlertDucking,
    scene_music::{MusicAction, SceneMusicEngine},
    spotify::{SpotifyClient, SpotifyQuery},
    ModuleError, ModuleResult,
};
use ccs_core::{
    spotify_states::{atomic_write, read_bytes, SavedPlaybackState, SpotifyStateStore},
    store::JsonSettingsStore,
};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{path::Path, sync::Arc};
use tokio::sync::{broadcast, Mutex};
#[derive(Default)]
struct Monitor {
    checked: Option<chrono::DateTime<chrono::Utc>>,
    recovered: Option<chrono::DateTime<chrono::Utc>>,
    cleaned: Option<chrono::DateTime<chrono::Utc>>,
    detail: String,
    error: Option<String>,
}

#[derive(Debug, Clone, Deserialize, Serialize)]
#[serde(
    tag = "action",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum MusicStateAction {
    Capture {
        group: String,
    },
    Restore {
        group: String,
        fade_seconds: u32,
    },
    Discard {
        group: String,
    },
    DiscardAll,
    Cleanup,
    HistoryEdit {
        entries: Vec<String>,
        favorite: Option<bool>,
        note: Option<String>,
        remove: bool,
    },
    HistoryClear,
    HistoryFilters {
        filters: Value,
    },
    Backup,
    BackupPreview {
        id: String,
    },
    BackupRestore {
        id: String,
        options: Value,
        original: Value,
    },
    BackupDelete {
        id: String,
    },
    HistoryImport {
        path: String,
    },
    HistoryExport {
        path: String,
        entries: Option<Vec<String>>,
        csv: bool,
    },
    ProfileSave {
        profile: Value,
    },
    ProfileDelete {
        name: String,
    },
    ProfilesPreview {
        path: String,
    },
    ProfilesImport {
        proposals: Value,
        actions: Vec<String>,
        original: Value,
    },
    ProfilesExport {
        path: String,
    },
}
pub struct SpotifyStateRuntime {
    store: Arc<SpotifyStateStore>,
    settings: Arc<JsonSettingsStore>,
    player: Arc<SpotifyClient>,
    engine: Arc<SceneMusicEngine>,
    ducking: Arc<AlertDucking>,
    changed: broadcast::Sender<()>,
    monitor: Mutex<Monitor>,
}
impl SpotifyStateRuntime {
    pub fn new(
        store: Arc<SpotifyStateStore>,
        settings: Arc<JsonSettingsStore>,
        player: Arc<SpotifyClient>,
        engine: Arc<SceneMusicEngine>,
        ducking: Arc<AlertDucking>,
    ) -> Self {
        Self {
            store,
            settings,
            player,
            engine,
            ducking,
            changed: broadcast::channel(32).0,
            monitor: Mutex::new(Monitor::default()),
        }
    }
    pub fn subscribe(&self) -> broadcast::Receiver<()> {
        self.changed.subscribe()
    }
    pub async fn snapshot(&self) -> ModuleResult<Value> {
        let mut snapshot = self.store.snapshot().await.map_err(error)?;
        let monitor = self.monitor.lock().await;
        snapshot["health"] =
            json!({"detail":monitor.detail,"error":monitor.error,"lastRecovery":monitor.recovered});
        Ok(snapshot)
    }
    pub async fn tick_at(
        &self,
        now: chrono::DateTime<chrono::Utc>,
        startup: bool,
    ) -> ModuleResult<()> {
        let settings = self.settings.load().await.map_err(error)?;
        let options = &settings.spotify.extra;
        let mut monitor = self.monitor.lock().await;
        let cleanup_interval = options["SavedStateCleanupIntervalMinutes"]
            .as_u64()
            .unwrap_or(15)
            .clamp(1, 1440) as i64;
        if (startup && options["SavedStateCleanupOnStartup"] != false)
            || (options["SavedStateCleanupIntervalEnabled"] == true
                && monitor
                    .cleaned
                    .is_none_or(|last| now - last >= chrono::Duration::minutes(cleanup_interval)))
        {
            self.store.cleanup(ttl(options), now).await.map_err(error)?;
            monitor.cleaned = Some(now);
            let _ = self.changed.send(());
        }
        if settings.music_player.provider_id() != "spotify"
            || options["HealthMonitorEnabled"] == false
        {
            monitor.detail = "Musiküberwachung deaktiviert".into();
            return Ok(());
        }
        if self.player.status().await.state != crate::ConnectionState::Connected {
            monitor.detail = "Spotify nicht verbunden".into();
            return Ok(());
        }
        let interval = options["HealthCheckIntervalSeconds"]
            .as_u64()
            .unwrap_or(30)
            .clamp(5, 300) as i64;
        if monitor
            .checked
            .is_some_and(|last| now - last < chrono::Duration::seconds(interval))
        {
            return Ok(());
        }
        monitor.checked = Some(now);
        let playback = match self
            .player
            .query(&settings.spotify.client_id, SpotifyQuery::Playback, None)
            .await
        {
            Ok(value) => value,
            Err(error) => {
                monitor.error = Some(error.to_string());
                let _ = self.changed.send(());
                return Err(error);
            }
        };
        monitor.error = None;
        monitor.detail = if playback["device"].is_null() {
            "Kein aktives Gerät"
        } else if playback["device"]["is_restricted"] == true {
            "Gerät nicht steuerbar"
        } else if playback["is_playing"] == true {
            "Wiedergabe aktiv"
        } else {
            "Bereit / pausiert"
        }
        .into();
        if options["AutoRecoverPlayback"] != false
            && playback["device"].is_null()
            && monitor
                .recovered
                .is_none_or(|last| now - last >= chrono::Duration::minutes(2))
        {
            monitor.recovered = Some(now);
            match self
                .player
                .activate_preferred_device(&settings.spotify.client_id, options, false)
                .await
            {
                Ok(device) => {
                    monitor.detail = format!(
                        "Gerät '{}' wieder aktiviert",
                        device["name"].as_str().unwrap_or("Spotify")
                    )
                }
                Err(error) => {
                    monitor.error = Some(error.to_string());
                    let _ = self.changed.send(());
                    return Err(error);
                }
            }
        }
        let _ = self.changed.send(());
        Ok(())
    }
    pub async fn action(&self, action: MusicStateAction) -> ModuleResult<Value> {
        let result = self.perform(action).await;
        let _ = self.changed.send(());
        result
    }
    async fn perform(&self, action: MusicStateAction) -> ModuleResult<Value> {
        match action {
            MusicStateAction::Capture { group } => {
                let settings = self.settings.load().await.map_err(error)?;
                if settings.spotify.extra["SavedStateCleanupOnSave"] != false {
                    self.store
                        .cleanup(ttl(&settings.spotify.extra), chrono::Utc::now())
                        .await
                        .map_err(error)?;
                }
                let playback = self
                    .player
                    .query(&settings.spotify.client_id, SpotifyQuery::Playback, None)
                    .await?;
                let item = &playback["item"];
                if item.is_null()
                    || item["type"].as_str().is_some_and(|kind| kind != "track")
                    || !item["uri"]
                        .as_str()
                        .is_some_and(|uri| uri.starts_with("spotify:track:"))
                {
                    return Err(ModuleError::Message(
                        "Kein aktiver Spotify-Titel zum Sichern vorhanden".into(),
                    ));
                }
                let desired = self.ducking.desired_volume().await;
                let volume = desired
                    .filter(|(id, _)| playback["device"]["id"] == *id)
                    .map(|(_, v)| v)
                    .unwrap_or(
                        playback["device"]["volume_percent"]
                            .as_u64()
                            .unwrap_or(0)
                            .min(100) as u8,
                    );
                let artists = item["artists"]
                    .as_array()
                    .map(|rows| {
                        rows.iter()
                            .filter_map(|a| a["name"].as_str())
                            .collect::<Vec<_>>()
                            .join(", ")
                    })
                    .unwrap_or_default();
                let track = json!({"Id":item["id"].as_str().unwrap_or(""),"Uri":item["uri"].as_str().unwrap_or(""),"Name":item["name"].as_str().unwrap_or(""),"Artist":artists,"Album":item["album"]["name"].as_str().unwrap_or(""),"AlbumImageUrl":item["album"]["images"][0]["url"].as_str().unwrap_or(""),"DurationMs":item["duration_ms"].as_u64().unwrap_or(0)});
                let state = SavedPlaybackState {
                    context_uri: playback["context"]["uri"].as_str().unwrap_or("").into(),
                    track,
                    progress_ms: playback["progress_ms"]
                        .as_u64()
                        .unwrap_or(0)
                        .min(u32::MAX as u64) as u32,
                    volume_percent: volume,
                    shuffle_enabled: playback["shuffle_state"] == true,
                    repeat_mode: playback["repeat_state"]
                        .as_str()
                        .filter(|s| !s.is_empty())
                        .unwrap_or("off")
                        .into(),
                    was_playing: playback["is_playing"] == true,
                    saved_at_utc: chrono::Utc::now(),
                    extra: Default::default(),
                };
                self.store.save(&group, state).await.map_err(error)?;
            }
            MusicStateAction::Restore {
                group,
                fade_seconds,
            } => {
                if fade_seconds > 300 {
                    return Err(ModuleError::Message(
                        "Wiederherstellungs-Fade darf höchstens 300 Sekunden dauern".into(),
                    ));
                }
                let state = self.store.get(&group).await.map_err(error)?;
                self.engine
                    .run(MusicAction::RestoreState {
                        state: serde_json::to_value(&state).map_err(error)?,
                        fade_seconds,
                    })
                    .await?;
                self.store.consume(&group,&state).await.map_err(|e|ModuleError::Message(format!("Wiedergabe wiederhergestellt; Zustand konnte nicht aktualisiert werden: {e}")))?;
            }
            MusicStateAction::Discard { group } => {
                self.store.remove(&group).await.map_err(error)?;
            }
            MusicStateAction::DiscardAll => {
                self.store.clear_states().await.map_err(error)?;
            }
            MusicStateAction::Cleanup => {
                let settings = self.settings.load().await.map_err(error)?;
                self.store
                    .cleanup(ttl(&settings.spotify.extra), chrono::Utc::now())
                    .await
                    .map_err(error)?;
            }
            MusicStateAction::HistoryEdit {
                entries,
                favorite,
                note,
                remove,
            } => self
                .store
                .edit_history(&entries, favorite, note.as_deref(), remove)
                .await
                .map_err(error)?,
            MusicStateAction::HistoryClear => self.store.clear_history().await.map_err(error)?,
            MusicStateAction::HistoryFilters { filters } => {
                self.store.set_filters(filters).await.map_err(error)?
            }
            MusicStateAction::Backup => {
                return self
                    .store
                    .backup()
                    .await
                    .map(|id| json!({"id":id}))
                    .map_err(error)
            }
            MusicStateAction::BackupPreview { id } => {
                return self.store.preview_backup(&id).await.map_err(error)
            }
            MusicStateAction::BackupRestore {
                id,
                options,
                original,
            } => self
                .store
                .restore_history(&id, options, &original)
                .await
                .map_err(error)?,
            MusicStateAction::BackupDelete { id } => {
                self.store.delete_backup(&id).await.map_err(error)?
            }
            MusicStateAction::HistoryImport { path } => {
                let text = read_text(&path).await?;
                self.store.import_history(&text).await.map_err(error)?;
            }
            MusicStateAction::HistoryExport { path, entries, csv } => {
                let bytes = self
                    .store
                    .export_history(entries.as_deref(), csv)
                    .await
                    .map_err(error)?;
                atomic_write(Path::new(&path), &bytes)
                    .await
                    .map_err(error)?;
            }
            MusicStateAction::ProfileSave { profile } => {
                self.store.save_profile(profile).await.map_err(error)?
            }
            MusicStateAction::ProfileDelete { name } => {
                self.store.delete_profile(&name).await.map_err(error)?
            }
            MusicStateAction::ProfilesPreview { path } => {
                return self
                    .store
                    .preview_profiles_import(&read_text(&path).await?)
                    .await
                    .map_err(error)
            }
            MusicStateAction::ProfilesImport {
                proposals,
                actions,
                original,
            } => self
                .store
                .import_profiles(&proposals, &actions, &original)
                .await
                .map_err(error)?,
            MusicStateAction::ProfilesExport { path } => {
                let value = self.store.export_profiles().await.map_err(error)?;
                atomic_write(
                    Path::new(&path),
                    &serde_json::to_vec_pretty(&value).map_err(error)?,
                )
                .await
                .map_err(error)?;
            }
        }
        Ok(json!({"success":true}))
    }
}
pub fn ttl(options: &Value) -> u32 {
    options["SavedStateMaxAgeMinutes"]
        .as_u64()
        .unwrap_or(180)
        .clamp(1, 10080) as u32
}
fn error(e: impl std::fmt::Display) -> ModuleError {
    ModuleError::Message(e.to_string())
}
async fn read_text(path: &str) -> ModuleResult<String> {
    String::from_utf8(read_bytes(Path::new(path)).await.map_err(error)?).map_err(error)
}
