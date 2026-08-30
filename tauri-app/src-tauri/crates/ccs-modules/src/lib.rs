pub mod alerts;
pub mod obs;
pub mod overlay_bridge;
pub mod sidecar;
pub mod spotify;
pub mod twitch;

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ConnectionState {
    Disconnected,
    Connecting,
    Connected,
    Error,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceStatus {
    pub id: String,
    pub name: String,
    pub state: ConnectionState,
    pub detail: String,
}

impl ServiceStatus {
    pub fn disconnected(id: &str, name: &str) -> Self {
        Self {
            id: id.into(),
            name: name.into(),
            state: ConnectionState::Disconnected,
            detail: String::new(),
        }
    }
}

#[derive(Debug, thiserror::Error)]
pub enum ModuleError {
    #[error("{0}")]
    Message(String),
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    #[error("http: {0}")]
    Http(#[from] reqwest::Error),
}

pub type ModuleResult<T> = Result<T, ModuleError>;
