use std::{future::Future, pin::Pin};
pub type OverlayFuture<'a, T> = Pin<Box<dyn Future<Output = Result<T, String>> + Send + 'a>>;
pub trait ObsOverlayProvider: Send + Sync {
    fn video_settings(&self) -> OverlayFuture<'_, serde_json::Value>;
    fn preview(&self) -> OverlayFuture<'_, Vec<u8>>;
}
