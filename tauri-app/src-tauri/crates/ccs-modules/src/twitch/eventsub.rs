use crate::overlay_bridge::flatten_event_data;
use crate::{ModuleError, ModuleResult};
use chrono::{DateTime, Utc};
use futures_util::{SinkExt, StreamExt};
use serde_json::Value;
use std::collections::BTreeMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;
use tokio::net::TcpStream;
use tokio::sync::{broadcast, Mutex};
use tokio::task::JoinHandle;
use tokio_tungstenite::{connect_async, tungstenite::Message, MaybeTlsStream, WebSocketStream};
use tracing::warn;

use super::helix::TwitchHelixClient;

type WsStream = WebSocketStream<MaybeTlsStream<TcpStream>>;
type WsWriter = futures_util::stream::SplitSink<WsStream, Message>;
type WsReader = futures_util::stream::SplitStream<WsStream>;

const CONNECT_TIMEOUT: Duration = Duration::from_secs(8);

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TwitchEvent {
    pub event_type: String,
    pub summary: String,
    pub received_at: DateTime<Utc>,
    pub data: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum EventSubMessage {
    Welcome { session_id: String },
    Keepalive,
    Reconnect { reconnect_url: String },
    Revocation,
    Notification(TwitchEvent),
    Unknown,
}

pub fn parse_eventsub_message(raw: &str) -> ModuleResult<EventSubMessage> {
    parse_eventsub_message_at(raw, Utc::now())
}

pub fn parse_eventsub_message_at(
    raw: &str,
    received_at: DateTime<Utc>,
) -> ModuleResult<EventSubMessage> {
    let root: Value = serde_json::from_str(raw)
        .map_err(|e| ModuleError::Message(format!("Ungültige EventSub-Nachricht: {e}")))?;
    let message_type = root
        .pointer("/metadata/message_type")
        .and_then(|v| v.as_str())
        .unwrap_or("");

    match message_type {
        "session_welcome" => {
            let session_id = root
                .pointer("/payload/session/id")
                .and_then(|v| v.as_str())
                .filter(|s| !s.is_empty())
                .ok_or_else(|| ModuleError::Message("Twitch EventSub Session-ID fehlt.".into()))?;
            Ok(EventSubMessage::Welcome {
                session_id: session_id.to_string(),
            })
        }
        "session_keepalive" => Ok(EventSubMessage::Keepalive),
        "session_reconnect" => {
            let url = root
                .pointer("/payload/session/reconnect_url")
                .and_then(|v| v.as_str())
                .filter(|s| !s.is_empty())
                .ok_or_else(|| {
                    ModuleError::Message("Twitch EventSub reconnect_url fehlt.".into())
                })?;
            Ok(EventSubMessage::Reconnect {
                reconnect_url: url.to_string(),
            })
        }
        "revocation" => Ok(EventSubMessage::Revocation),
        "notification" => {
            let event_type = root
                .pointer("/payload/subscription/type")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let event_data = root
                .pointer("/payload/event")
                .cloned()
                .unwrap_or(Value::Object(Default::default()));
            let data = flatten_event_data(&event_data);
            let summary = create_summary(&event_type, &data);
            Ok(EventSubMessage::Notification(TwitchEvent {
                event_type,
                summary,
                received_at,
                data,
            }))
        }
        _ => Ok(EventSubMessage::Unknown),
    }
}

pub fn create_summary(event_type: &str, data: &BTreeMap<String, String>) -> String {
    fn get(data: &BTreeMap<String, String>, key: &str) -> String {
        data.get(key).cloned().unwrap_or_default()
    }

    match event_type {
        "channel.follow" => format!("{} folgt dem Kanal.", get(data, "user_name")),
        "channel.subscribe" => format!("{} hat abonniert.", get(data, "user_name")),
        "channel.subscription.message" => {
            format!("{} hat erneut abonniert.", get(data, "user_name"))
        }
        "channel.subscription.gift" => format!("{} verschenkt Subs.", get(data, "user_name")),
        "channel.cheer" => format!(
            "{} cheeret {} Bits.",
            get(data, "user_name"),
            get(data, "bits")
        ),
        "channel.raid" => format!(
            "{} raidet mit {} Zuschauern.",
            get(data, "from_broadcaster_user_name"),
            get(data, "viewers")
        ),
        "channel.guest_star_guest.update" => {
            let guest = get(data, "guest_user_name");
            match get(data, "state").as_str() {
                "invited" => format!("Stream-Together-Anfrage für {guest}."),
                "accepted" => format!("{guest} hat Stream Together angenommen."),
                "ready" => format!("{guest} ist für Stream Together bereit."),
                "live" => format!("{guest} ist jetzt in Stream Together live."),
                "removed" => format!("{guest} hat Stream Together verlassen."),
                other => format!("Stream Together: {guest} ({other})."),
            }
        }
        "stream.online" => "Der Stream ist online.".into(),
        "stream.offline" => "Der Stream ist offline.".into(),
        "revocation" => "Eine Twitch EventSub-Subscription wurde widerrufen.".into(),
        _ => event_type.to_string(),
    }
}

pub fn alert_type_for_event(event_type: &str) -> Option<&'static str> {
    match event_type {
        "channel.follow" => Some("Follow"),
        "channel.subscribe" => Some("Sub"),
        "channel.subscription.message" => Some("ReSub"),
        "channel.subscription.gift" => Some("GiftSub"),
        "channel.cheer" => Some("Cheer"),
        "channel.raid" => Some("Raid"),
        _ => None,
    }
}

struct LiveSession {
    writer: WsWriter,
}

/// EventSub WebSocket client: welcome, subscriptions, keepalive, reconnect.
pub struct EventSubClient {
    cancel: AtomicBool,
    session: Mutex<Option<LiveSession>>,
    receive_task: Mutex<Option<JoinHandle<()>>>,
}

impl EventSubClient {
    pub fn new() -> Self {
        Self {
            cancel: AtomicBool::new(false),
            session: Mutex::new(None),
            receive_task: Mutex::new(None),
        }
    }

    pub fn new_shared() -> Arc<Self> {
        Arc::new(Self::new())
    }

    pub async fn connect(
        self: &Arc<Self>,
        url: &str,
        helix: TwitchHelixClient,
        broadcaster_user_id: &str,
        user_id: &str,
        events_tx: broadcast::Sender<TwitchEvent>,
    ) -> ModuleResult<()> {
        self.stop().await;
        self.cancel.store(false, Ordering::SeqCst);

        let (writer, mut reader) = connect_split(url).await?;
        let welcome = read_text_frame(&mut reader, "session_welcome").await?;
        let parsed = parse_eventsub_message(&welcome)?;
        let EventSubMessage::Welcome { session_id } = parsed else {
            return Err(ModuleError::Message(
                "Twitch EventSub sendete keine session_welcome-Nachricht.".into(),
            ));
        };

        let active = subscribe_events(
            &helix,
            broadcaster_user_id,
            user_id,
            &session_id,
            &events_tx,
        )
        .await?;
        if active == 0 {
            let _ = writer;
            return Err(ModuleError::Message(
                "Twitch EventSub konnte keine Chat- oder Event-Abonnements anlegen. \
Bitte Twitch erneut autorisieren und die benötigten Berechtigungen bestätigen."
                    .into(),
            ));
        }

        *self.session.lock().await = Some(LiveSession { writer });
        self.spawn_receive(reader, events_tx).await;
        Ok(())
    }

    async fn spawn_receive(
        self: &Arc<Self>,
        mut reader: WsReader,
        events_tx: broadcast::Sender<TwitchEvent>,
    ) {
        if let Some(handle) = self.receive_task.lock().await.take() {
            handle.abort();
        }
        let this = Arc::clone(self);
        *self.receive_task.lock().await = Some(tokio::spawn(async move {
            loop {
                if this.cancel.load(Ordering::SeqCst) {
                    break;
                }
                match read_text_frame_timeout(&mut reader, "notification", Duration::from_secs(30))
                    .await
                {
                    Ok(text) => match parse_eventsub_message(&text) {
                        Ok(EventSubMessage::Keepalive) | Ok(EventSubMessage::Unknown) => {}
                        Ok(EventSubMessage::Notification(evt)) => {
                            let _ = events_tx.send(evt);
                        }
                        Ok(EventSubMessage::Revocation) => {
                            let _ = events_tx.send(TwitchEvent {
                                event_type: "revocation".into(),
                                summary: create_summary("revocation", &BTreeMap::new()),
                                received_at: Utc::now(),
                                data: BTreeMap::new(),
                            });
                        }
                        Ok(EventSubMessage::Reconnect { reconnect_url }) => {
                            match reconnect(&mut reader, &this, &reconnect_url).await {
                                Ok(()) => {}
                                Err(e) => {
                                    warn!(error = %e, "EventSub reconnect failed");
                                    break;
                                }
                            }
                        }
                        Ok(EventSubMessage::Welcome { .. }) => {}
                        Err(e) => {
                            warn!(error = %e, "EventSub parse error");
                        }
                    },
                    Err(_) => break,
                }
            }
        }));
    }

    pub async fn stop(&self) {
        self.cancel.store(true, Ordering::SeqCst);
        if let Some(handle) = self.receive_task.lock().await.take() {
            handle.abort();
        }
        if let Some(mut sess) = self.session.lock().await.take() {
            let _ = sess.writer.close().await;
        }
    }
}

impl Default for EventSubClient {
    fn default() -> Self {
        Self::new()
    }
}

async fn reconnect(
    reader: &mut WsReader,
    client: &EventSubClient,
    reconnect_url: &str,
) -> ModuleResult<()> {
    let (writer, mut new_reader) = connect_split(reconnect_url).await?;
    let welcome = read_text_frame(&mut new_reader, "session_welcome").await?;
    match parse_eventsub_message(&welcome)? {
        EventSubMessage::Welcome { .. } => {}
        _ => {
            return Err(ModuleError::Message(
                "Twitch EventSub sendete keine session_welcome-Nachricht.".into(),
            ));
        }
    }
    if let Some(mut old) = client.session.lock().await.replace(LiveSession { writer }) {
        let _ = old.writer.close().await;
    }
    *reader = new_reader;
    Ok(())
}

async fn connect_split(url: &str) -> ModuleResult<(WsWriter, WsReader)> {
    let (ws, _) = match tokio::time::timeout(CONNECT_TIMEOUT, connect_async(url)).await {
        Ok(Ok(pair)) => pair,
        Ok(Err(e)) => return Err(ModuleError::Message(e.to_string())),
        Err(_) => {
            return Err(ModuleError::Message(
                "Twitch EventSub-Verbindungstimeout".into(),
            ));
        }
    };
    Ok(ws.split())
}

async fn read_text_frame(reader: &mut WsReader, label: &str) -> ModuleResult<String> {
    read_text_frame_timeout(reader, label, CONNECT_TIMEOUT).await
}

async fn read_text_frame_timeout(
    reader: &mut WsReader,
    label: &str,
    timeout: Duration,
) -> ModuleResult<String> {
    loop {
        match tokio::time::timeout(timeout, reader.next()).await {
            Ok(Some(Ok(Message::Text(text)))) => return Ok(text.to_string()),
            Ok(Some(Ok(Message::Ping(_)))) => continue,
            Ok(Some(Ok(_))) => {
                return Err(ModuleError::Message(format!(
                    "Twitch EventSub sendete kein Text-Frame bei {label}."
                )));
            }
            Ok(Some(Err(e))) => return Err(ModuleError::Message(e.to_string())),
            Ok(None) => {
                return Err(ModuleError::Message(
                    "Twitch EventSub hat die Verbindung geschlossen.".into(),
                ));
            }
            Err(_) => {
                return Err(ModuleError::Message(format!(
                    "Twitch EventSub {label}-Timeout"
                )));
            }
        }
    }
}

async fn subscribe_events(
    helix: &TwitchHelixClient,
    broadcaster_user_id: &str,
    user_id: &str,
    session_id: &str,
    events_tx: &broadcast::Sender<TwitchEvent>,
) -> ModuleResult<usize> {
    let specs: [(&str, &str, Value, bool); 9] = [
        (
            "channel.follow",
            "2",
            serde_json::json!({
                "broadcaster_user_id": broadcaster_user_id,
                "moderator_user_id": user_id
            }),
            false,
        ),
        (
            "channel.subscribe",
            "1",
            serde_json::json!({ "broadcaster_user_id": broadcaster_user_id }),
            false,
        ),
        (
            "channel.subscription.message",
            "1",
            serde_json::json!({ "broadcaster_user_id": broadcaster_user_id }),
            false,
        ),
        (
            "channel.subscription.gift",
            "1",
            serde_json::json!({ "broadcaster_user_id": broadcaster_user_id }),
            false,
        ),
        (
            "channel.cheer",
            "1",
            serde_json::json!({ "broadcaster_user_id": broadcaster_user_id }),
            false,
        ),
        (
            "channel.raid",
            "1",
            serde_json::json!({ "to_broadcaster_user_id": broadcaster_user_id }),
            false,
        ),
        (
            "channel.guest_star_guest.update",
            "beta",
            serde_json::json!({
                "broadcaster_user_id": broadcaster_user_id,
                "moderator_user_id": broadcaster_user_id
            }),
            true,
        ),
        (
            "stream.online",
            "1",
            serde_json::json!({ "broadcaster_user_id": broadcaster_user_id }),
            false,
        ),
        (
            "stream.offline",
            "1",
            serde_json::json!({ "broadcaster_user_id": broadcaster_user_id }),
            false,
        ),
    ];

    let mut active = 0usize;
    for (ty, version, condition, silent) in specs {
        match helix
            .create_eventsub_subscription(ty, version, condition, session_id)
            .await
        {
            Ok(()) => active += 1,
            Err(e) if silent => {
                warn!(subscription = ty, error = %e, "optional EventSub skipped");
            }
            Err(e) => {
                let _ = events_tx.send(TwitchEvent {
                    event_type: "subscription.warning".into(),
                    summary: format!("{ty} konnte nicht aktiviert werden: {e}"),
                    received_at: Utc::now(),
                    data: BTreeMap::from([
                        ("subscription_type".into(), ty.to_string()),
                        ("error".into(), e.to_string()),
                    ]),
                });
            }
        }
    }
    Ok(active)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::twitch::TwitchHelixClient;
    use futures_util::SinkExt;
    use serde_json::json;
    use tokio::net::TcpListener;
    use tokio_tungstenite::accept_async;
    use wiremock::matchers::{header, method, path};
    use wiremock::{Mock, MockServer, ResponseTemplate};

    fn fixture(name: &str) -> String {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("src/twitch/fixtures")
            .join(name);
        std::fs::read_to_string(path).expect("fixture")
    }

    #[test]
    fn session_welcome_extracts_id() {
        let msg = parse_eventsub_message(&fixture("session-welcome.json")).unwrap();
        assert_eq!(
            msg,
            EventSubMessage::Welcome {
                session_id: "session-contract-id".into()
            }
        );
    }

    #[test]
    fn session_keepalive_is_noop() {
        let msg = parse_eventsub_message(&fixture("session-keepalive.json")).unwrap();
        assert_eq!(msg, EventSubMessage::Keepalive);
    }

    #[test]
    fn session_reconnect_extracts_url() {
        let msg = parse_eventsub_message(&fixture("session-reconnect.json")).unwrap();
        match msg {
            EventSubMessage::Reconnect { reconnect_url } => {
                assert!(reconnect_url.contains("reconnect-token"));
            }
            other => panic!("expected reconnect, got {other:?}"),
        }
    }

    #[test]
    fn revocation_maps() {
        let msg = parse_eventsub_message(&fixture("revocation.json")).unwrap();
        assert_eq!(msg, EventSubMessage::Revocation);
    }

    #[test]
    fn notification_follow_maps_summary_and_data() {
        let at = DateTime::parse_from_rfc3339("2026-07-27T18:00:00Z")
            .unwrap()
            .with_timezone(&Utc);
        let msg = parse_eventsub_message_at(&fixture("notification-follow.json"), at).unwrap();
        match msg {
            EventSubMessage::Notification(evt) => {
                assert_eq!(evt.event_type, "channel.follow");
                assert_eq!(evt.summary, "alice folgt dem Kanal.");
                assert_eq!(evt.data.get("user_name").map(String::as_str), Some("alice"));
                assert_eq!(evt.data.get("user_id").map(String::as_str), Some("1"));
            }
            other => panic!("expected notification, got {other:?}"),
        }
    }

    #[test]
    fn notification_subscribe_and_cheer_summaries() {
        let sub = parse_eventsub_message(&fixture("notification-subscribe.json")).unwrap();
        match sub {
            EventSubMessage::Notification(evt) => {
                assert_eq!(evt.event_type, "channel.subscribe");
                assert_eq!(evt.summary, "bob hat abonniert.");
            }
            other => panic!("{other:?}"),
        }
        let cheer = parse_eventsub_message(&fixture("notification-cheer.json")).unwrap();
        match cheer {
            EventSubMessage::Notification(evt) => {
                assert_eq!(evt.event_type, "channel.cheer");
                assert_eq!(evt.summary, "carol cheeret 500 Bits.");
                assert_eq!(evt.data.get("bits").map(String::as_str), Some("500"));
            }
            other => panic!("{other:?}"),
        }
        let raid = parse_eventsub_message(&fixture("notification-raid.json")).unwrap();
        match raid {
            EventSubMessage::Notification(evt) => {
                assert_eq!(evt.event_type, "channel.raid");
                assert_eq!(evt.summary, "Raider raidet mit 42 Zuschauern.");
            }
            other => panic!("{other:?}"),
        }
    }

    #[tokio::test]
    async fn helix_creates_eventsub_subscription_with_headers() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/eventsub/subscriptions"))
            .and(header("Authorization", "Bearer contract-token"))
            .and(header("Client-Id", "contract-client"))
            .respond_with(ResponseTemplate::new(202).set_body_json(json!({ "data": [] })))
            .mount(&server)
            .await;

        let client = TwitchHelixClient::with_base_url(
            format!("{}/", server.uri()),
            "contract-client",
            "contract-token",
        );
        client
            .create_eventsub_subscription(
                "channel.follow",
                "2",
                json!({
                    "broadcaster_user_id": "141981764",
                    "moderator_user_id": "141981764"
                }),
                "session-contract-id",
            )
            .await
            .unwrap();
    }

    #[tokio::test]
    async fn eventsub_client_publishes_follow_from_mock_ws() {
        let helix_server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/eventsub/subscriptions"))
            .respond_with(ResponseTemplate::new(202).set_body_json(json!({ "data": [] })))
            .mount(&helix_server)
            .await;

        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let follow = fixture("notification-follow.json");
        let welcome = fixture("session-welcome.json");

        let server = tokio::spawn(async move {
            let (stream, _) = listener.accept().await.unwrap();
            let mut ws = accept_async(stream).await.unwrap();
            ws.send(Message::Text(welcome.into())).await.unwrap();
            tokio::time::sleep(Duration::from_millis(150)).await;
            ws.send(Message::Text(follow.into())).await.unwrap();
            tokio::time::sleep(Duration::from_millis(200)).await;
            let _ = ws.close(None).await;
        });

        let helix = TwitchHelixClient::with_base_url(
            format!("{}/", helix_server.uri()),
            "contract-client",
            "contract-token",
        );
        let (tx, mut rx) = broadcast::channel(16);
        let client = EventSubClient::new_shared();
        client
            .connect(
                &format!("ws://127.0.0.1:{port}"),
                helix,
                "141981764",
                "141981764",
                tx,
            )
            .await
            .unwrap();

        let evt = tokio::time::timeout(Duration::from_secs(3), rx.recv())
            .await
            .expect("timeout")
            .expect("event");
        assert_eq!(evt.event_type, "channel.follow");
        assert_eq!(evt.summary, "alice folgt dem Kanal.");
        client.stop().await;
        let _ = server.await;
    }
}
