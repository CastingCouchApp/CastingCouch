use ccs_overlay_server::RealtimeHub;
use chrono::{DateTime, Utc};
use serde::Serialize;
use serde_json::Value;
use std::collections::BTreeMap;
use std::sync::Arc;

/// Overlay `/ws` envelope matching WPF `OverlayRealtimeEvent` (camelCase).
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OverlayRealtimeEvent {
    pub source: String,
    #[serde(rename = "type")]
    pub event_type: String,
    pub at: DateTime<Utc>,
    pub summary: String,
    pub data: BTreeMap<String, String>,
}

impl OverlayRealtimeEvent {
    pub fn new(
        source: impl Into<String>,
        event_type: impl Into<String>,
        at: DateTime<Utc>,
        summary: impl Into<String>,
        data: BTreeMap<String, String>,
    ) -> Self {
        Self {
            source: source.into(),
            event_type: event_type.into(),
            at,
            summary: summary.into(),
            data,
        }
    }

    pub fn to_value(&self) -> Value {
        serde_json::to_value(self).unwrap_or(Value::Null)
    }
}

#[derive(Clone)]
pub struct OverlayEventBridge {
    hub: Arc<RealtimeHub>,
}

impl OverlayEventBridge {
    pub fn new(hub: Arc<RealtimeHub>) -> Self {
        Self { hub }
    }

    pub fn publish(&self, event: &OverlayRealtimeEvent) -> Value {
        let value = event.to_value();
        self.hub.publish(&value);
        value
    }

    pub fn from_twitch(
        &self,
        event_type: &str,
        summary: &str,
        at: DateTime<Utc>,
        data: BTreeMap<String, String>,
    ) -> Value {
        self.publish(&OverlayRealtimeEvent::new(
            "twitch", event_type, at, summary, data,
        ))
    }

    pub fn app_obs_scene(&self, scene: &str) -> Value {
        self.publish(&app_event(
            "app.obs.scene",
            &format!("Szene: {scene}"),
            map_of([("scene", scene)]),
        ))
    }

    pub fn app_alert(&self, alert_type: &str, user: &str) -> Value {
        let summary = if user.trim().is_empty() {
            alert_type.to_string()
        } else {
            format!("{alert_type}: {user}")
        };
        self.publish(&app_event(
            "app.alert",
            &summary,
            map_of([("alertType", alert_type), ("user", user)]),
        ))
    }

    pub fn music_track(&self, title: &str, artist: &str) -> Value {
        self.app_music_track("spotify", title, artist, "")
    }

    pub fn app_music_track(
        &self,
        provider: &str,
        title: &str,
        artist: &str,
        cover_url: &str,
    ) -> Value {
        let summary = if artist.trim().is_empty() {
            title.to_string()
        } else {
            format!("{artist} – {title}")
        };
        let display = music_provider_display(provider);
        self.publish(&app_event(
            "app.music.track",
            &summary,
            map_of([
                ("provider", provider),
                ("providerDisplayName", display),
                ("title", title),
                ("artist", artist),
                ("coverUrl", cover_url),
            ]),
        ))
    }

    pub fn countdown(&self, remaining_seconds: i64) -> Value {
        let remaining = remaining_seconds.max(0);
        self.publish(&app_event(
            "app.countdown",
            &format!("Countdown: {remaining}s"),
            map_of([
                ("isRunning", "true"),
                ("remainingSeconds", &remaining.to_string()),
                ("totalSeconds", "0"),
                ("label", ""),
                ("endsAt", ""),
            ]),
        ))
    }

    pub fn layout_changed(&self, canvas_id: &str) -> Value {
        self.publish(&app_event(
            "app.overlay.layout",
            &format!("Layout: {canvas_id}"),
            map_of([("instanceId", canvas_id), ("layout", "")]),
        ))
    }
}

fn app_event(
    event_type: &str,
    summary: &str,
    data: BTreeMap<String, String>,
) -> OverlayRealtimeEvent {
    OverlayRealtimeEvent::new("app", event_type, Utc::now(), summary, data)
}

fn map_of<'a>(pairs: impl IntoIterator<Item = (&'a str, &'a str)>) -> BTreeMap<String, String> {
    pairs
        .into_iter()
        .map(|(k, v)| (k.to_string(), v.to_string()))
        .collect()
}

fn music_provider_display(provider: &str) -> &'static str {
    match provider {
        "ytmusic" => "YouTube Music",
        "spotify" => "Spotify",
        _ => "Music",
    }
}

/// Flatten a JSON object into string map values (EventSub `event` payload).
pub fn flatten_event_data(value: &Value) -> BTreeMap<String, String> {
    let mut data = BTreeMap::new();
    let Some(obj) = value.as_object() else {
        return data;
    };
    for (key, val) in obj {
        data.insert(key.clone(), json_to_string(val));
    }
    data
}

fn json_to_string(value: &Value) -> String {
    match value {
        Value::String(s) => s.clone(),
        Value::Null => String::new(),
        other => other.to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    fn at() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 7, 27, 18, 0, 0).unwrap()
    }

    #[tokio::test]
    async fn from_twitch_maps_eventsub_fields() {
        let hub = Arc::new(RealtimeHub::new());
        let mut rx = hub.subscribe();
        let bridge = OverlayEventBridge::new(hub);
        let mut data = BTreeMap::new();
        data.insert("user_name".into(), "alice".into());
        data.insert("user_id".into(), "1".into());

        let published = bridge.from_twitch("channel.follow", "alice folgt jetzt", at(), data);

        assert_eq!(published["source"], "twitch");
        assert_eq!(published["type"], "channel.follow");
        assert_eq!(published["summary"], "alice folgt jetzt");
        assert_eq!(published["data"]["user_name"], "alice");
        assert_eq!(published["data"]["user_id"], "1");
        assert!(published["at"]
            .as_str()
            .unwrap()
            .starts_with("2026-07-27T18:00:00"));

        let frame = rx.recv().await.expect("hub frame");
        let root: Value = serde_json::from_str(&frame).unwrap();
        assert_eq!(root["source"], "twitch");
        assert_eq!(root["type"], "channel.follow");
        assert_eq!(root["data"]["user_name"], "alice");
    }

    #[tokio::test]
    async fn app_obs_scene_builds_typed_event() {
        let hub = Arc::new(RealtimeHub::new());
        let mut rx = hub.subscribe();
        let bridge = OverlayEventBridge::new(hub);
        let published = bridge.app_obs_scene("Game");
        assert_eq!(published["source"], "app");
        assert_eq!(published["type"], "app.obs.scene");
        assert_eq!(published["data"]["scene"], "Game");
        let frame = rx.recv().await.expect("hub frame");
        assert!(frame.contains("\"scene\":\"Game\"") || frame.contains("\"scene\": \"Game\""));
    }

    #[test]
    fn app_alert_builds_typed_event() {
        let hub = Arc::new(RealtimeHub::new());
        let _rx = hub.subscribe();
        let bridge = OverlayEventBridge::new(hub);
        let published = bridge.app_alert("Follow", "alice");
        assert_eq!(published["source"], "app");
        assert_eq!(published["type"], "app.alert");
        assert_eq!(published["data"]["alertType"], "Follow");
        assert_eq!(published["data"]["user"], "alice");
    }

    #[test]
    fn music_track_uses_envelope() {
        let hub = Arc::new(RealtimeHub::new());
        let _rx = hub.subscribe();
        let bridge = OverlayEventBridge::new(hub);
        let published = bridge.music_track("Song", "Artist");
        assert_eq!(published["source"], "app");
        assert_eq!(published["type"], "app.music.track");
        assert_eq!(published["data"]["title"], "Song");
        assert_eq!(published["data"]["artist"], "Artist");
        assert_eq!(published["data"]["provider"], "spotify");
    }

    #[test]
    fn countdown_and_layout_use_envelope() {
        let hub = Arc::new(RealtimeHub::new());
        let _rx = hub.subscribe();
        let bridge = OverlayEventBridge::new(hub);
        let countdown = bridge.countdown(12);
        assert_eq!(countdown["type"], "app.countdown");
        assert_eq!(countdown["data"]["remainingSeconds"], "12");
        let layout = bridge.layout_changed("default");
        assert_eq!(layout["type"], "app.overlay.layout");
        assert_eq!(layout["data"]["instanceId"], "default");
    }
}
