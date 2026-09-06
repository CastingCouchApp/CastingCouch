mod assets;
mod canvas;
mod hub;
mod layout_store;
mod live;
mod providers;
mod routes;
mod state;
pub use providers::{ObsOverlayProvider, OverlayFuture};
mod library;
mod ytmusic;
pub use library::MediaLibrary;
pub use ytmusic::{MusicSnapshot, YouTubeMusicBridge};

use axum::Router;
use ccs_core::{AppPaths, JsonSettingsStore};
use std::net::SocketAddr;
use std::sync::Arc;
use tokio::sync::RwLock;
use tracing::info;

pub use assets::embedded_asset;
pub use canvas::{CanvasError, CanvasSettingsPersist, OverlayCanvasService};
pub use hub::RealtimeHub;
pub use layout_store::OverlayLayoutStore;
pub use state::OverlayState;

#[derive(Debug, thiserror::Error)]
pub enum OverlayServerError {
    #[error("bind {addr}: {source}")]
    Bind {
        addr: SocketAddr,
        #[source]
        source: std::io::Error,
    },
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
}

pub struct OverlayServer {
    pub port: u16,
    task: tokio::task::JoinHandle<()>,
    shutdown: tokio::sync::watch::Sender<bool>,
}

impl OverlayServer {
    pub async fn start(
        settings: Arc<JsonSettingsStore>,
        paths: AppPaths,
        hub: Arc<RealtimeHub>,
        port: u16,
    ) -> Result<Self, OverlayServerError> {
        hub.configure_history(paths.overlay_root.join("chat-history.json"))
            .map_err(std::io::Error::other)?;
        let addr = SocketAddr::from(([127, 0, 0, 1], port));
        let listener = tokio::net::TcpListener::bind(addr)
            .await
            .map_err(|source| OverlayServerError::Bind { addr, source })?;
        let bound = listener.local_addr()?;
        info!(port = bound.port(), "overlay server listening");

        let overlay_data = paths.data_root.join("data/overlay-data.json");
        let (shutdown_tx, mut shutdown_rx) = tokio::sync::watch::channel(false);
        let state = OverlayState {
            port: bound.port(),
            shutdown: Some(shutdown_rx.clone()),
            settings,
            paths,
            hub,
            overlay_data,
            clients: Arc::new(RwLock::new(0)),
        };

        let app = routes::router(state);
        let task = tokio::spawn(async move {
            axum::serve(listener, app)
                .with_graceful_shutdown(async move {
                    let _ = shutdown_rx.wait_for(|v| *v).await;
                })
                .await
                .ok();
        });

        Ok(Self {
            port: bound.port(),
            shutdown: shutdown_tx,
            task,
        })
    }

    pub fn is_running(&self) -> bool {
        !self.task.is_finished() && !*self.shutdown.borrow()
    }

    pub fn stop(&self) {
        let _ = self.shutdown.send(true);
    }
}

pub fn router_for_tests(state: OverlayState) -> Router {
    routes::router(state)
}
