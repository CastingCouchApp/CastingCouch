use crate::hub::RealtimeHub;
use ccs_core::{AppPaths, JsonSettingsStore};
use std::path::PathBuf;
use std::sync::Arc;
use tokio::sync::RwLock;

#[derive(Clone)]
pub struct OverlayState {
    pub settings: Arc<JsonSettingsStore>,
    pub paths: AppPaths,
    pub hub: Arc<RealtimeHub>,
    pub overlay_data: PathBuf,
    pub clients: Arc<RwLock<usize>>,
}
