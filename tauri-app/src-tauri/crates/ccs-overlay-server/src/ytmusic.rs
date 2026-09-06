use axum::{
    extract::{DefaultBodyLimit, State},
    response::Html,
    routing::{get, post},
    Json, Router,
};
use serde::{Deserialize, Serialize};
use serde_json::json;
use std::{
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};
use tower_http::cors::{Any, CorsLayer};

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(default, rename_all = "camelCase")]
pub struct MusicSnapshot {
    pub provider: String,
    pub connected: bool,
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
}

impl YouTubeMusicBridge {
    pub async fn start(port: u16) -> Result<Arc<Self>, String> {
        let listener = tokio::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, port))
            .await
            .map_err(|e| format!("YouTube-Music-Port {port}: {e}"))?;
        let (shutdown, mut stopped) = tokio::sync::watch::channel(false);
        let bridge = Arc::new(Self {
            port: listener.local_addr().map_err(|e| e.to_string())?.port(),
            state: Mutex::new((MusicSnapshot::default(), None, vec![])),
            shutdown,
        });
        let router = Router::new()
            .route("/ytmusic/state", post(|State(bridge): State<Arc<Self>>, Json(snapshot): Json<MusicSnapshot>| async move {
                bridge.receive(snapshot); Json(json!({"ok":true}))
            }))
            .route("/ytmusic/health", get(|| async { Json(json!({"ok":true,"running":true})) }))
            .route("/ytmusic/commands", get(|State(bridge): State<Arc<Self>>| async move {
                let commands = std::mem::take(&mut bridge.state.lock().unwrap().2);
                Json(json!({"commands":commands}))
            }))
            .route("/ytmusic/install", get(|State(bridge): State<Arc<Self>>| async move { Html(bridge.install_html()) }))
            .route("/ytmusic/bookmarklet.js", get(|State(bridge): State<Arc<Self>>| async move { ([("content-type", "application/javascript")], bridge.script()) }))
            .layer(DefaultBodyLimit::max(64 * 1024))
            .layer(CorsLayer::new().allow_origin(Any).allow_methods(Any).allow_headers(Any))
            .with_state(bridge.clone());
        tokio::spawn(async move {
            let _ = axum::serve(listener, router)
                .with_graceful_shutdown(async move {
                    let _ = stopped.wait_for(|v| *v).await;
                })
                .await;
        });
        Ok(bridge)
    }
    pub fn receive(&self, mut snapshot: MusicSnapshot) {
        snapshot.provider = "ytmusic".into();
        snapshot.connected = true;
        snapshot.progress_ms = snapshot.progress_ms.max(0);
        snapshot.duration_ms = snapshot.duration_ms.max(0);
        snapshot.status_text = if snapshot.is_playing {
            "Spielt"
        } else {
            "Pausiert"
        }
        .into();
        let mut state = self.state.lock().unwrap();
        state.0 = snapshot;
        state.1 = Some(Instant::now());
    }
    pub fn snapshot(&self) -> MusicSnapshot {
        let state = self.state.lock().unwrap();
        if state
            .1
            .is_some_and(|at| at.elapsed() < Duration::from_secs(15))
        {
            state.0.clone()
        } else {
            MusicSnapshot {
                provider: "ytmusic".into(),
                status_text: "Bookmarklet inaktiv".into(),
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
        if state.2.len() >= 32 {
            return Err("Zu viele ausstehende Musikbefehle.".into());
        }
        state.2.push(command.to_string());
        Ok(())
    }
    pub fn stop(&self) {
        let _ = self.shutdown.send(true);
    }
    fn script(&self) -> String {
        include_str!(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../../../src/CreatorControlSuite.Modules.YouTubeMusic/Assets/ytmusic-bridge.js"
        ))
        .replace("__CCS_BRIDGE_PORT__", &self.port.to_string())
    }
    fn install_html(&self) -> String {
        let encoded: String = self.script().bytes().map(|b| format!("%{b:02X}")).collect();
        format!("<!doctype html><html lang=de><meta charset=utf-8><title>YouTube Music verbinden</title><body style='font:18px system-ui;max-width:640px;margin:60px auto'><h1>YouTube Music verbinden</h1><p>Ziehe den Link in die Lesezeichenleiste. Öffne music.youtube.com und klicke dort auf das Lesezeichen. Der Tab muss geöffnet bleiben.</p><a href=\"javascript:{encoded}\">CCS · YouTube Music</a></body></html>")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
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
