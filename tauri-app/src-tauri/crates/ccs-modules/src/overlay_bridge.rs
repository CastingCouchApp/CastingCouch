use ccs_overlay_server::RealtimeHub;
use chrono::{DateTime, Utc};
use serde::Serialize;
use serde_json::Value;
use std::collections::{BTreeMap, VecDeque};
use std::sync::{Arc, Mutex};

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
    twitch_feed: Arc<Mutex<VecDeque<OverlayRealtimeEvent>>>,
    twitch_chat: Arc<ccs_overlay_server::ChatHistoryBuffer>,
}

#[derive(Debug, Clone, Serialize)]
pub struct TwitchEventFeedSnapshot {
    pub events: Vec<OverlayRealtimeEvent>,
}

impl OverlayEventBridge {
    pub fn new(hub: Arc<RealtimeHub>) -> Self {
        Self {
            hub,
            twitch_feed: Arc::new(Mutex::new(VecDeque::new())),
            twitch_chat: Arc::new(ccs_overlay_server::ChatHistoryBuffer::with_capacity(500)),
        }
    }

    /// C# keeps the last 200 EventReceived entries in app memory, separate from chat.
    pub fn twitch_event_feed(&self) -> TwitchEventFeedSnapshot {
        TwitchEventFeedSnapshot {
            events: self.twitch_feed.lock().unwrap().iter().cloned().collect(),
        }
    }

    pub fn twitch_chat_feed(&self) -> Value {
        self.twitch_chat.history()
    }

    pub fn publish(&self, event: &OverlayRealtimeEvent) -> Value {
        if event.source.eq_ignore_ascii_case("twitch") && event.event_type != "channel.chat.message"
        {
            let mut feed = self.twitch_feed.lock().unwrap();
            feed.push_back(event.clone());
            while feed.len() > 200 {
                feed.pop_front();
            }
        }
        let value = event.to_value();
        self.twitch_chat.record(&value);
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

    pub fn app_chat_config(&self) -> Value {
        self.publish(&app_event(
            "app.chat.config",
            "Chat-Einstellungen geändert",
            map_of([]),
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

    pub fn app_alert_rendered(
        &self,
        alert_type: &str,
        user: &str,
        text: &str,
        variables: &BTreeMap<String, String>,
    ) -> Value {
        let mut data = variables.clone();
        data.insert("alertType".into(), alert_type.into());
        data.insert("user".into(), user.into());
        data.insert("text".into(), text.into());
        self.publish(&app_event(
            "app.alert",
            &format!("{alert_type}: {user}"),
            data,
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

    #[test]
    fn twitch_feed_keeps_the_last_200_non_chat_events_in_receipt_order() {
        let hub = Arc::new(RealtimeHub::new());
        let bridge = OverlayEventBridge::new(hub.clone());
        for index in 0..205 {
            bridge.from_twitch(
                "channel.follow",
                &format!("Follower {index}"),
                at(),
                map_of([("index", index.to_string().as_str())]),
            );
            bridge.from_twitch(
                "channel.chat.message",
                "Chat",
                at(),
                map_of([("messageId", index.to_string().as_str())]),
            );
            bridge.app_alert("Follow", "Alice");
        }
        let snapshot = serde_json::to_value(bridge.twitch_event_feed()).unwrap();
        let events = snapshot["events"].as_array().unwrap();
        assert_eq!(events.len(), 200);
        assert_eq!(events[0]["summary"], "Follower 5");
        assert_eq!(events[199]["data"]["index"], "204");
        assert!(events
            .iter()
            .all(|e| e["source"] == "twitch" && e["type"] == "channel.follow"));
        assert_eq!(hub.history()["events"].as_array().unwrap().len(), 160);
        assert_eq!(
            serde_json::to_value(bridge.clone().twitch_event_feed()).unwrap(),
            snapshot
        );
        assert!(
            serde_json::to_value(OverlayEventBridge::new(hub).twitch_event_feed()).unwrap()
                ["events"]
                .as_array()
                .unwrap()
                .is_empty()
        );
    }

    #[test]
    fn twitch_feed_retains_warnings_moderation_and_future_event_types_without_rewriting_payloads() {
        let bridge = OverlayEventBridge::new(Arc::new(RealtimeHub::new()));
        let mut expected = Vec::new();
        for ty in [
            "subscription.warning",
            "revocation",
            "channel.chat.clear",
            "channel.chat.message_delete",
            "channel.chat.clear_user_messages",
            "channel.future.event",
        ] {
            expected.push(bridge.from_twitch(
                ty,
                "<b>keine HTML-Ausführung</b>",
                at(),
                map_of([("custom", "äö🎉"), ("nested", "{\"field\":42}")]),
            ));
        }
        assert_eq!(
            serde_json::to_value(bridge.twitch_event_feed()).unwrap()["events"],
            serde_json::json!(expected)
        );
    }

    #[test]
    fn app_chat_keeps_500_messages_independently_of_overlay_capacity_and_applies_moderation() {
        let hub = Arc::new(RealtimeHub::new());
        let bridge = OverlayEventBridge::new(hub.clone());
        for index in 0..600 {
            bridge.from_twitch(
                "channel.chat.message",
                "Hallo",
                at(),
                map_of([
                    ("messageId", index.to_string().as_str()),
                    ("userLogin", if index % 2 == 0 { "Alice" } else { "Bob" }),
                    ("userId", if index % 2 == 0 { "a" } else { "b" }),
                    ("parts", "[{\"type\":\"text\",\"text\":\"Hallo\"}]"),
                ]),
            );
        }
        assert_eq!(
            bridge.twitch_chat_feed()["events"]
                .as_array()
                .unwrap()
                .len(),
            500
        );
        assert_eq!(
            bridge.twitch_chat_feed()["events"][0]["data"]["messageId"],
            "100"
        );
        assert_eq!(
            bridge.twitch_chat_feed()["events"][0]["data"]["parts"],
            "[{\"type\":\"text\",\"text\":\"Hallo\"}]"
        );
        assert_eq!(hub.history()["events"].as_array().unwrap().len(), 160);
        hub.configure_chat_buffer(2000);
        assert_eq!(
            bridge.twitch_chat_feed()["events"]
                .as_array()
                .unwrap()
                .len(),
            500
        );
        bridge.from_twitch(
            "channel.chat.message_delete",
            "Gelöscht",
            at(),
            map_of([("message_id", " 599 ")]),
        );
        assert_eq!(
            bridge.twitch_chat_feed()["events"]
                .as_array()
                .unwrap()
                .len(),
            499
        );
        bridge.from_twitch(
            "channel.chat.clear_user_messages",
            "Timeout",
            at(),
            map_of([("target_user_login", " alice ")]),
        );
        assert_eq!(
            bridge.twitch_chat_feed()["events"]
                .as_array()
                .unwrap()
                .len(),
            249
        );
        assert!(bridge.twitch_chat_feed()["events"]
            .as_array()
            .unwrap()
            .iter()
            .all(|e| e["data"]["userLogin"] == "Bob"));
        bridge.from_twitch(
            "channel.chat.message",
            "Doppeltes Ereignis",
            at(),
            map_of([("messageId", "101")]),
        );
        assert_eq!(
            bridge.twitch_chat_feed()["events"]
                .as_array()
                .unwrap()
                .len(),
            249
        );
        bridge.from_twitch("channel.chat.clear", "Chat geleert", at(), map_of([]));
        assert_eq!(
            bridge.clone().twitch_chat_feed()["events"],
            serde_json::json!([])
        );
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
