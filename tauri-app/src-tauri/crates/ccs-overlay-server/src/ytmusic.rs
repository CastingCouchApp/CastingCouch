use axum::{
    extract::{DefaultBodyLimit, State},
    response::Html,
    routing::{get, post},
    Json, Router,
};
use serde::{Deserialize, Serialize};
use serde_json::json;
use std::{
    sync::{
        atomic::{AtomicBool, AtomicU64, Ordering},
        Arc, Mutex,
    },
    time::{Duration, Instant},
};
use tower_http::cors::{Any, CorsLayer};

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(default, rename_all = "camelCase")]
pub struct MusicSnapshot {
    pub provider: String,
    pub connected: bool,
    pub bridge_running: bool,
    pub is_playing: bool,
    pub title: String,
    pub artist: String,
    pub album: String,
    pub cover_url: String,
    pub progress_ms: i64,
    pub duration_ms: i64,
    pub status_text: String,
}

pub struct YouTubeMusicBridge {
    pub port: u16,
    state: Mutex<(MusicSnapshot, Option<Instant>, Vec<String>)>,
    shutdown: tokio::sync::watch::Sender<bool>,
    running: Arc<AtomicBool>,
    timeout_seconds: AtomicU64,
    task: Mutex<Option<tokio::task::JoinHandle<()>>>,
    server_error: Arc<Mutex<Option<String>>>,
}

impl YouTubeMusicBridge {
    pub async fn start(port: u16) -> Result<Arc<Self>, String> {
        Self::start_with_timeout(port, 12).await
    }
    pub async fn start_with_timeout(port: u16, timeout_seconds: u64) -> Result<Arc<Self>, String> {
        let listener = tokio::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, port))
            .await
            .map_err(|e| format!("YouTube-Music-Port {port}: {e}"))?;
        let (shutdown, mut stopped) = tokio::sync::watch::channel(false);
        let bridge = Arc::new(Self {
            port: listener.local_addr().map_err(|e| e.to_string())?.port(),
            state: Mutex::new((MusicSnapshot::default(), None, vec![])),
            shutdown,
            running: Arc::new(AtomicBool::new(true)),
            timeout_seconds: AtomicU64::new(timeout_seconds.clamp(3, 120)),
            task: Mutex::new(None),
            server_error: Arc::new(Mutex::new(None)),
        });
        let router = Router::new()
            .route("/ytmusic/state", post(|State(bridge): State<Arc<Self>>, Json(snapshot): Json<MusicSnapshot>| async move {
                bridge.receive(snapshot); Json(json!({"ok":true}))
            }))
            .route("/ytmusic/health", get(|State(bridge): State<Arc<Self>>| async move { Json(json!({"ok":bridge.is_running(),"running":bridge.is_running()})) }))
            .route("/ytmusic/commands", get(|State(bridge): State<Arc<Self>>| async move {
                let commands = std::mem::take(&mut bridge.state.lock().unwrap().2);
                Json(json!({"commands":commands}))
            }))
            .route("/ytmusic/install", get(|State(bridge): State<Arc<Self>>| async move { Html(bridge.install_html()) }))
            .route("/ytmusic/bookmarklet.js", get(|State(bridge): State<Arc<Self>>| async move { ([("content-type", "application/javascript")], bridge.script()) }))
            .layer(DefaultBodyLimit::max(64 * 1024))
            .layer(CorsLayer::new().allow_origin(Any).allow_methods(Any).allow_headers(Any))
            .with_state(bridge.clone());
        let running = bridge.running.clone();
        let server_error = bridge.server_error.clone();
        let task = tokio::spawn(async move {
            if let Err(error) = axum::serve(listener, router)
                .with_graceful_shutdown(async move {
                    let _ = stopped.wait_for(|v| *v).await;
                })
                .await
            {
                *server_error.lock().unwrap() = Some(error.to_string());
            }
            running.store(false, Ordering::SeqCst);
        });
        *bridge.task.lock().unwrap() = Some(task);
        Ok(bridge)
    }
    pub fn is_running(&self) -> bool {
        self.running.load(Ordering::SeqCst)
    }
    pub fn error(&self) -> Option<String> {
        self.server_error.lock().unwrap().clone()
    }
    pub fn set_timeout_seconds(&self, seconds: u64) {
        self.timeout_seconds
            .store(seconds.clamp(3, 120), Ordering::SeqCst);
    }
    pub fn receive(&self, mut snapshot: MusicSnapshot) {
        if !self.is_running() {
            return;
        }
        snapshot.provider = "ytmusic".into();
        snapshot.connected = true;
        snapshot.bridge_running = true;
        snapshot.progress_ms = snapshot.progress_ms.max(0);
        snapshot.duration_ms = snapshot.duration_ms.max(0);
        snapshot.status_text = if snapshot.is_playing {
            "Spielt"
        } else {
            "Pausiert"
        }
        .into();
        let mut state = self.state.lock().unwrap();
        if !self.is_running() {
            return;
        }
        state.0 = snapshot;
        state.1 = Some(Instant::now());
    }
    pub fn snapshot(&self) -> MusicSnapshot {
        let mut state = self.state.lock().unwrap();
        let running = self.is_running();
        if running
            && state.1.is_some_and(|at| {
                at.elapsed() <= Duration::from_secs(self.timeout_seconds.load(Ordering::SeqCst))
            })
        {
            state.0.clone()
        } else {
            state.2.clear();
            MusicSnapshot {
                provider: "ytmusic".into(),
                bridge_running: running,
                status_text: if running {
                    "Bookmarklet inaktiv"
                } else {
                    "Bridge gestoppt"
                }
                .into(),
                ..Default::default()
            }
        }
    }
    pub fn command(&self, command: &str) -> Result<(), String> {
        if !["play", "pause", "playpause", "next", "previous"].contains(&command) {
            return Err("YouTube Music unterstützt diesen Befehl nicht.".into());
        }
        if !self.snapshot().connected {
            return Err("YouTube-Music-Bookmarklet ist nicht verbunden.".into());
        }
        let mut state = self.state.lock().unwrap();
        if !self.is_running() {
            return Err("YouTube-Music-Bridge ist gestoppt.".into());
        }
        if state.2.len() >= 32 {
            return Err("Zu viele ausstehende Musikbefehle.".into());
        }
        state.2.push(command.to_string());
        Ok(())
    }
    pub fn stop(&self) {
        self.running.store(false, Ordering::SeqCst);
        *self.state.lock().unwrap() = (MusicSnapshot::default(), None, vec![]);
        let _ = self.shutdown.send(true);
    }
    pub async fn stop_and_wait(&self) {
        self.stop();
        let task = self.task.lock().unwrap().take();
        if let Some(mut task) = task {
            if tokio::time::timeout(Duration::from_secs(2), &mut task)
                .await
                .is_err()
            {
                task.abort();
                let _ = task.await;
            }
        }
    }
    pub fn install_url(&self) -> String {
        format!("http://127.0.0.1:{}/ytmusic/install", self.port)
    }
    pub fn bookmarklet(&self) -> String {
        let encoded: String = self.script().bytes().map(|b| format!("%{b:02X}")).collect();
        format!("javascript:{encoded}")
    }
    fn script(&self) -> String {
        include_str!(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../../../src/CreatorControlSuite.Modules.YouTubeMusic/Assets/ytmusic-bridge.js"
        ))
        .replace("__CCS_BRIDGE_PORT__", &self.port.to_string())
    }
    fn install_html(&self) -> String {
        let bookmarklet = self.bookmarklet();
        format!("<!doctype html><html lang=de><meta charset=utf-8><title>YouTube Music verbinden</title><body style='font:18px system-ui;max-width:640px;margin:60px auto'><h1>YouTube Music verbinden</h1><p>Ziehe den Link in die Lesezeichenleiste. Öffne music.youtube.com und klicke dort auf das Lesezeichen. Der Tab muss geöffnet bleiben.</p><a href=\"{bookmarklet}\">CCS · YouTube Music</a></body></html>")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[tokio::test]
    async fn configured_timeout_and_shutdown_clear_stale_state_and_release_listener() {
        let bridge = YouTubeMusicBridge::start_with_timeout(0, 30).await.unwrap();
        bridge.receive(MusicSnapshot {
            title: "Track".into(),
            ..Default::default()
        });
        bridge.state.lock().unwrap().1 = Some(Instant::now() - Duration::from_secs(20));
        assert!(bridge.snapshot().connected);
        bridge.command("next").unwrap();
        bridge.set_timeout_seconds(12);
        assert!(!bridge.snapshot().connected);
        assert!(bridge.snapshot().bridge_running);
        assert!(bridge.state.lock().unwrap().2.is_empty());
        let decoded = bridge.script();
        assert!(decoded.contains('\n'));
        assert!(decoded.contains("tick();"));
        assert!(decoded.contains(&bridge.port.to_string()));
        assert!(bridge.bookmarklet().starts_with("javascript:%"));
        assert!(!decoded.contains("__CCS_BRIDGE_PORT__"));
        bridge.stop_and_wait().await;
        assert!(!bridge.is_running());
        assert!(!bridge.snapshot().bridge_running);
        assert!(bridge.command("play").is_err());
        let rebound = tokio::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, bridge.port))
            .await
            .unwrap();
        drop(rebound);
    }
    #[tokio::test]
    async fn native_bridge_reports_state_and_delivers_commands() {
        let bridge = YouTubeMusicBridge::start(0).await.unwrap();
        assert!(!bridge.snapshot().connected);
        assert!(bridge.command("play").is_err());
        bridge.receive(MusicSnapshot {
            title: "Song".into(),
            is_playing: true,
            ..Default::default()
        });
        assert_eq!(bridge.snapshot().title, "Song");
        bridge.command("pause").unwrap();
        assert_eq!(bridge.state.lock().unwrap().2, ["pause"]);
        assert!(bridge.command("delete").is_err());
        bridge.state.lock().unwrap().1 = Some(Instant::now() - Duration::from_secs(16));
        assert!(!bridge.snapshot().connected);
        assert!(bridge.install_html().contains("javascript:%"));
        bridge.stop();
    }
}
