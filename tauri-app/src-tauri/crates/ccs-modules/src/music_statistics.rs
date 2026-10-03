use crate::{
    spotify::{PlaybackSample, SpotifyClient},
    ModuleError, ModuleResult,
};
use ccs_core::{
    music_statistics::{ListeningSample, ListeningTrack, MusicStatisticsStore},
    store::JsonSettingsStore,
};
use serde_json::{json, Value};
use std::{
    sync::atomic::{AtomicBool, Ordering},
    sync::Arc,
};
use tokio::sync::{broadcast, watch, Mutex};

pub struct MusicStatisticsRuntime {
    store: Arc<MusicStatisticsStore>,
    settings: Arc<JsonSettingsStore>,
    gate: Mutex<()>,
    error: Mutex<Option<String>>,
    changed: broadcast::Sender<()>,
    closed: AtomicBool,
    stopped: watch::Sender<bool>,
}
impl MusicStatisticsRuntime {
    pub fn new(store: Arc<MusicStatisticsStore>, settings: Arc<JsonSettingsStore>) -> Self {
        Self {
            store,
            settings,
            gate: Mutex::new(()),
            error: Mutex::new(None),
            changed: broadcast::channel(32).0,
            closed: AtomicBool::new(false),
            stopped: watch::channel(false).0,
        }
    }
    pub fn subscribe(&self) -> broadcast::Receiver<()> {
        self.changed.subscribe()
    }
    pub async fn snapshot(&self) -> ModuleResult<Value> {
        let mut data = serde_json::to_value(self.store.snapshot().await.map_err(module_error)?)
            .map_err(module_error)?;
        data["error"] = json!(*self.error.lock().await);
        Ok(data)
    }
    pub async fn reset(&self) -> ModuleResult<()> {
        let _guard = self.gate.lock().await;
        let result = self.store.reset().await.map_err(module_error);
        self.finish(&result).await;
        result
    }
    async fn finish(&self, result: &ModuleResult<()>) {
        *self.error.lock().await = result.as_ref().err().map(ToString::to_string);
        let _ = self.changed.send(());
    }
    async fn observe(&self, sample: PlaybackSample) {
        let _guard = self.gate.lock().await;
        if self.closed.load(Ordering::SeqCst) {
            return;
        }
        let result = async {
            let settings = self.settings.load().await.map_err(module_error)?;
            if settings.music_player.provider_id() != "spotify" || sample.playing.is_none() {
                self.store.suspend_at(sample.sampled_at).await;
                if let Some(error) = sample.error {
                    return Err(ModuleError::Message(error));
                }
                return Ok(());
            }
            let playing = sample.playing.unwrap();
            let track = if playing.track_id.trim().is_empty() {
                None
            } else {
                Some(ListeningTrack {
                    id: playing.track_id,
                    title: playing.title,
                    artist: playing.artist,
                    album: playing.album,
                })
            };
            self.store
                .observe_at(
                    ListeningSample {
                        track,
                        is_playing: playing.is_playing,
                    },
                    sample.sampled_at,
                )
                .await
                .map_err(module_error)
        }
        .await;
        if result.is_err() {
            self.store.suspend_at(chrono::Utc::now()).await;
        }
        self.finish(&result).await;
    }
    pub fn bind_spotify(
        self: &Arc<Self>,
        player: &SpotifyClient,
    ) -> impl std::future::Future<Output = ()> + Send + 'static {
        let mut samples = player.subscribe_playback_samples();
        let mut stopped = self.stopped.subscribe();
        let runtime = self.clone();
        async move {
            loop {
                if runtime.closed.load(Ordering::SeqCst) {
                    break;
                }
                tokio::select! { biased;
                    _=stopped.changed()=>break,
                    sample=samples.recv()=>match sample {
                        Ok(sample)=>runtime.observe(sample).await,
                        Err(broadcast::error::RecvError::Lagged(_))=>{
                            let _guard=runtime.gate.lock().await;
                            runtime.observe_lag().await;
                        },
                        Err(broadcast::error::RecvError::Closed)=>break,
                    }
                }
            }
        }
    }
    async fn observe_lag(&self) {
        self.store.suspend_at(chrono::Utc::now()).await;
        *self.error.lock().await =
            Some("Spotify-Messungen übersprungen; Hörzeit wurde nicht hochgerechnet".into());
        let _ = self.changed.send(());
    }
    pub async fn close(&self) {
        self.closed.store(true, Ordering::SeqCst);
        let _ = self.stopped.send(true);
        let _guard = self.gate.lock().await;
        self.store.suspend_at(chrono::Utc::now()).await;
    }
}
fn module_error(e: impl std::fmt::Display) -> ModuleError {
    ModuleError::Message(e.to_string())
}
