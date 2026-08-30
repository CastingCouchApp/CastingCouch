use ccs_overlay_server::RealtimeHub;
use serde_json::{json, Value};
use std::sync::Arc;

pub struct OverlayEventBridge {
    hub: Arc<RealtimeHub>,
}

impl OverlayEventBridge {
    pub fn new(hub: Arc<RealtimeHub>) -> Self {
        Self { hub }
    }

    pub fn music_track(&self, title: &str, artist: &str) {
        self.hub.publish(&json!({
            "type": "app.music.track",
            "title": title,
            "artist": artist,
        }));
    }

    pub fn twitch_event(&self, event: &Value) {
        let mut payload = event.clone();
        if payload.get("type").is_none() {
            payload["type"] = json!("app.twitch.event");
        }
        self.hub.publish(&payload);
    }

    pub fn countdown(&self, remaining_seconds: i64) {
        self.hub.publish(&json!({
            "type": "app.countdown",
            "remainingSeconds": remaining_seconds,
        }));
    }

    pub fn layout_changed(&self, canvas_id: &str) {
        self.hub.publish(&json!({
            "type": "app.overlay.layout",
            "id": canvas_id,
        }));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn publishes_without_panic() {
        let hub = Arc::new(RealtimeHub::new());
        let bridge = OverlayEventBridge::new(hub);
        bridge.music_track("Song", "Artist");
        bridge.countdown(12);
        bridge.layout_changed("default");
    }
}
