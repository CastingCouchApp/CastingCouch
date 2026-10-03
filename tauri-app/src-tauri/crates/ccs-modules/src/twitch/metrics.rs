use super::{TwitchClient, TwitchQuery};
use crate::ConnectionState;
use ccs_core::JsonSettingsStore;
use ccs_overlay_server::RealtimeHub;
use chrono::Utc;
use serde::Serialize;
use serde_json::{json, Value};
use std::{
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc,
    },
    time::{Duration, Instant},
};
use tokio::sync::{Mutex, RwLock};

#[derive(Debug, Default, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TwitchCount {
    pub value: Option<u64>,
    pub at: Option<String>,
    pub error: Option<String>,
}
#[derive(Debug, Default, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TwitchMetricsSnapshot {
    pub connected: bool,
    pub viewer_count: TwitchCount,
    pub followers: TwitchCount,
    pub subscriptions: TwitchCount,
    pub chatters: TwitchCount,
    pub title: String,
    pub category: String,
    pub channel_error: Option<String>,
}
#[derive(Default)]
struct MetricsState {
    snapshot: TwitchMetricsSnapshot,
    signature: Option<(String, String)>,
    last_refresh: Option<Instant>,
    chatter_due: Option<Instant>,
    channel: Value,
    stream: Value,
    followers: Value,
    subscriptions: Value,
}
pub struct TwitchMetricsRuntime {
    settings: Arc<JsonSettingsStore>,
    twitch: Arc<TwitchClient>,
    hub: Arc<RealtimeHub>,
    operation: Mutex<()>,
    state: RwLock<MetricsState>,
    event_refresh: AtomicBool,
}
impl TwitchMetricsRuntime {
    pub fn new(
        settings: Arc<JsonSettingsStore>,
        twitch: Arc<TwitchClient>,
        hub: Arc<RealtimeHub>,
    ) -> Self {
        Self {
            settings,
            twitch,
            hub,
            operation: Mutex::new(()),
            state: RwLock::new(MetricsState::default()),
            event_refresh: AtomicBool::new(false),
        }
    }
    pub async fn snapshot(&self) -> TwitchMetricsSnapshot {
        self.state.read().await.snapshot.clone()
    }
    pub fn notify_event(&self, event_type: &str) -> bool {
        let relevant = matches!(
            event_type,
            "channel.follow"
                | "channel.subscribe"
                | "channel.subscription.message"
                | "channel.subscription.gift"
                | "channel.subscription.end"
                | "stream.online"
                | "stream.offline"
        );
        if relevant {
            self.event_refresh.store(true, Ordering::Release);
        }
        relevant
    }
    pub async fn apply_goals(&self, settings: &Value) {
        // Finish an older HTTP refresh before publishing the newly saved configuration.
        let _guard = self.operation.lock().await;
        self.hub.live.update_goal_settings(settings);
    }
    pub async fn refresh(&self, force: bool) -> Result<bool, String> {
        let _guard = self.operation.lock().await;
        let force = self.event_refresh.swap(false, Ordering::AcqRel) || force;
        let settings = self.settings.load().await.map_err(|e| e.to_string())?;
        let signature = (
            settings.twitch.client_id.clone(),
            settings.twitch.channel_name.clone(),
        );
        let connected = self.twitch.status().await.state == ConnectionState::Connected;
        let now = Instant::now();
        let (regular, chatters) = {
            let state = self.state.read().await;
            let changed = state.signature.as_ref() != Some(&signature)
                || state.snapshot.connected != connected;
            (
                force
                    || changed
                    || state
                        .last_refresh
                        .is_none_or(|last| now.duration_since(last) >= Duration::from_secs(30)),
                force || changed || state.chatter_due.is_none_or(|due| now >= due),
            )
        };
        if !regular && !chatters {
            return Ok(false);
        }
        let query = |kind| async {
            if !connected {
                return Err("Twitch ist nicht verbunden.".to_owned());
            }
            tokio::time::timeout(
                Duration::from_secs(8),
                self.twitch.query(
                    &settings.twitch.client_id,
                    &settings.twitch.channel_name,
                    kind,
                    None,
                ),
            )
            .await
            .map_err(|_| "Twitch-Abfrage hat das Zeitlimit überschritten.".to_owned())?
            .map_err(|e| e.to_string())
        };
        let (channel, stream, followers, subscriptions, chatter_result) = tokio::join!(
            async {
                if regular {
                    Some(query(TwitchQuery::Channel).await)
                } else {
                    None
                }
            },
            async {
                if regular {
                    Some(query(TwitchQuery::Stream).await)
                } else {
                    None
                }
            },
            async {
                if regular {
                    Some(query(TwitchQuery::Followers).await)
                } else {
                    None
                }
            },
            async {
                if regular {
                    Some(query(TwitchQuery::Subscriptions).await)
                } else {
                    None
                }
            },
            async {
                if chatters {
                    Some(query(TwitchQuery::Chatters).await)
                } else {
                    None
                }
            }
        );
        // Apply new goal configuration, and reject responses for a channel changed during HTTP.
        let latest = self.settings.load().await.map_err(|e| e.to_string())?;
        if signature
            != (
                latest.twitch.client_id.clone(),
                latest.twitch.channel_name.clone(),
            )
            || connected != (self.twitch.status().await.state == ConnectionState::Connected)
        {
            return Ok(false);
        }
        let mut state = self.state.write().await;
        if state.signature.as_ref() != Some(&signature) {
            *state = MetricsState::default();
            self.hub.live.merge_snapshot(&json!({"twitch":{"followersKnown":false,"subscriptionsKnown":false,"followers":0,"subscriptions":0}}));
        }
        state.signature = Some(signature);
        state.snapshot.connected = connected;
        if let Some(result) = channel {
            match result {
                Ok(value) if value.pointer("/data/0").is_some_and(Value::is_object) => {
                    state.snapshot.title = value
                        .pointer("/data/0/title")
                        .and_then(Value::as_str)
                        .unwrap_or("")
                        .into();
                    state.snapshot.category = value
                        .pointer("/data/0/game_name")
                        .and_then(Value::as_str)
                        .unwrap_or("")
                        .into();
                    state.snapshot.channel_error = None;
                    state.channel = value;
                }
                Ok(_) => {
                    state.snapshot.channel_error =
                        Some("Twitch liefert keine Kanalinformationen.".into())
                }
                Err(error) => state.snapshot.channel_error = Some(error),
            }
        }
        if let Some(result) = stream {
            if let Some(value) = update_count(&mut state.snapshot.viewer_count, result, true) {
                state.stream = value;
            }
        }
        if let Some(result) = followers {
            if let Some(value) = update_count(&mut state.snapshot.followers, result, false) {
                state.followers = value;
            }
        }
        if let Some(result) = subscriptions {
            if let Some(value) = update_count(&mut state.snapshot.subscriptions, result, false) {
                state.subscriptions = value;
            }
        }
        if let Some(result) = chatter_result {
            update_count(&mut state.snapshot.chatters, result, false);
        }
        if regular {
            state.last_refresh = Some(Instant::now());
        }
        if chatters {
            state.chatter_due = Some(
                Instant::now()
                    + Duration::from_secs(chatter_interval(
                        &latest.twitch.extra,
                        state.snapshot.viewer_count.value.unwrap_or(0),
                    )),
            );
        }
        if regular {
            let valid = |value: &Value, ok: bool| if ok { value.clone() } else { Value::Null };
            self.hub.live.update_twitch(
                &serde_json::to_value(&latest.twitch).map_err(|e| e.to_string())?,
                &valid(&state.channel, state.snapshot.channel_error.is_none()),
                &valid(&state.stream, state.snapshot.viewer_count.error.is_none()),
                &valid(&state.followers, state.snapshot.followers.error.is_none()),
                &valid(
                    &state.subscriptions,
                    state.snapshot.subscriptions.error.is_none(),
                ),
            );
        }
        self.hub.live.merge_snapshot(&json!({"twitch":{
            "available":connected&&state.snapshot.channel_error.is_none(),
            "viewerCountAvailable":connected&&state.snapshot.viewer_count.error.is_none()&&state.snapshot.viewer_count.value.is_some(),
            "followersAvailable":connected&&state.snapshot.followers.error.is_none()&&state.snapshot.followers.value.is_some(),
            "subscriptionsAvailable":connected&&state.snapshot.subscriptions.error.is_none()&&state.snapshot.subscriptions.value.is_some(),
            "chattersAvailable":connected&&state.snapshot.chatters.error.is_none()&&state.snapshot.chatters.value.is_some(),
            "chatters":state.snapshot.chatters.value.unwrap_or(0),"metrics":state.snapshot
        }}));
        Ok(true)
    }
}
fn update_count(
    count: &mut TwitchCount,
    result: Result<Value, String>,
    stream: bool,
) -> Option<Value> {
    match result {
        Ok(value) => {
            let number = if stream {
                value["data"].as_array().and_then(|items| {
                    if items.is_empty() {
                        Some(0)
                    } else {
                        items[0]["viewer_count"].as_u64()
                    }
                })
            } else {
                value["total"].as_u64()
            };
            match number {
                Some(number) => {
                    count.value = Some(number);
                    count.at = Some(Utc::now().to_rfc3339());
                    count.error = None;
                    Some(value)
                }
                None => {
                    count.error = Some("Twitch liefert keinen gültigen Zähler.".into());
                    None
                }
            }
        }
        Err(error) => {
            count.error = Some(error);
            None
        }
    }
}
pub fn chatter_interval(settings: &Value, viewers: u64) -> u64 {
    let positive =
        |key: &str, default: u64| settings[key].as_u64().filter(|s| *s > 0).unwrap_or(default);
    let threshold = positive("ChattersRefreshViewerThreshold", 50).clamp(1, 10000);
    let low = positive("ChattersRefreshSecondsLow", 10).clamp(5, 120);
    let high = positive("ChattersRefreshSecondsHigh", 60)
        .clamp(15, 600)
        .max(low);
    if viewers >= threshold {
        high
    } else {
        low
    }
}
