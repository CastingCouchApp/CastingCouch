use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_modules::{
    overlay_bridge::OverlayEventBridge,
    twitch::{EventSubClient, TwitchHelixClient},
};
use ccs_overlay_server::{OverlayServer, RealtimeHub};
use futures_util::{SinkExt, StreamExt};
use serde_json::{json, Value};
use std::{sync::Arc, time::Duration};
use tokio::{net::TcpListener, sync::broadcast};
use tokio_tungstenite::{accept_async, tungstenite::Message};
use wiremock::{
    matchers::{body_partial_json, method, path},
    Mock, MockServer, ResponseTemplate,
};

#[tokio::test]
async fn actual_eventsub_notifications_warnings_and_reconnect_reach_the_app_feed_and_overlay() {
    let http = MockServer::start().await;
    Mock::given(method("POST"))
        .and(path("/eventsub/subscriptions"))
        .respond_with(ResponseTemplate::new(202).set_body_json(json!({"data":[]})))
        .mount(&http)
        .await;
    Mock::given(body_partial_json(json!({"type":"channel.follow"})))
        .respond_with(ResponseTemplate::new(403).set_body_json(json!({"message":"Missing scope"})))
        .with_priority(1)
        .mount(&http)
        .await;
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let url = format!("ws://{}", listener.local_addr().unwrap());
    let (ready_tx, mut ready_rx) = tokio::sync::mpsc::channel(2);
    let upstream = tokio::spawn(async move {
        for session in 0..2 {
            let (socket, _) = listener.accept().await.unwrap();
            let mut ws = accept_async(socket).await.unwrap();
            ws.send(Message::Text(json!({"metadata":{"message_type":"session_welcome"},"payload":{"session":{"id":format!("session-{session}")}}}).to_string().into())).await.unwrap();
            ready_rx.recv().await.unwrap();
            let events = if session == 0 {
                vec![
                    (
                        "channel.subscribe",
                        json!({"user_name":"Alice","tier":"1000"}),
                    ),
                    (
                        "channel.chat.message",
                        json!({"chatter_user_name":"Alice","chatter_user_id":"u","message_id":"m","message":{"text":"Hallo 🎉","fragments":[{"type":"text","text":"Hallo 🎉"}]}}),
                    ),
                    (
                        "channel.raid",
                        json!({"from_broadcaster_user_name":"Bob","viewers":42}),
                    ),
                    ("channel.chat.message_delete", json!({"message_id":"m"})),
                    ("channel.future.event", json!({"custom":{"value":"äö"}})),
                ]
            } else {
                vec![(
                    "channel.follow",
                    json!({"user_name":"Nach Wiederverbindung"}),
                )]
            };
            for (ty, event) in events {
                ws.send(Message::Text(json!({"metadata":{"message_type":"notification"},"payload":{"subscription":{"type":ty},"event":event}}).to_string().into())).await.unwrap();
            }
            ws.send(Message::Text(
                json!({"metadata":{"message_type":"revocation"}})
                    .to_string()
                    .into(),
            ))
            .await
            .unwrap();
            while let Some(frame) = ws.next().await {
                if matches!(frame, Ok(Message::Close(_)) | Err(_)) {
                    break;
                }
            }
        }
    });
    let root = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(root.path().into());
    let hub = Arc::new(RealtimeHub::new());
    let overlay = OverlayServer::start(
        Arc::new(JsonSettingsStore::new(&paths.settings_file)),
        paths,
        hub.clone(),
        0,
    )
    .await
    .unwrap();
    let (mut overlay_ws, _) =
        tokio_tungstenite::connect_async(format!("ws://127.0.0.1:{}/ws", overlay.port))
            .await
            .unwrap();
    for _ in 0..2 {
        tokio::time::timeout(Duration::from_secs(3), overlay_ws.next())
            .await
            .unwrap()
            .unwrap()
            .unwrap();
    }
    let bridge = OverlayEventBridge::new(hub.clone());
    let client = EventSubClient::new_shared();
    let mut expected = Vec::new();
    for _ in 0..2 {
        let (tx, mut rx) = broadcast::channel(32);
        client
            .connect(
                &url,
                TwitchHelixClient::with_base_url(format!("{}/", http.uri()), "client", "token"),
                "channel",
                "user",
                tx,
            )
            .await
            .unwrap();
        ready_tx.send(()).await.unwrap();
        loop {
            let event = tokio::time::timeout(Duration::from_secs(3), rx.recv())
                .await
                .unwrap()
                .unwrap();
            let value = bridge.from_twitch(
                &event.event_type,
                &event.summary,
                event.received_at,
                event.data,
            );
            let frame = tokio::time::timeout(Duration::from_secs(3), overlay_ws.next())
                .await
                .unwrap()
                .unwrap()
                .unwrap();
            assert_eq!(
                serde_json::from_str::<Value>(frame.to_text().unwrap()).unwrap(),
                value
            );
            if event.event_type != "channel.chat.message" {
                expected.push(value);
            }
            if event.event_type == "revocation" {
                break;
            }
        }
        client.stop().await;
        assert_eq!(
            serde_json::to_value(bridge.twitch_event_feed()).unwrap()["events"],
            json!(expected)
        );
    }
    assert_eq!(expected.len(), 9);
    assert!(expected[0]["summary"]
        .as_str()
        .unwrap()
        .contains("Missing scope"));
    assert_eq!(expected[0]["data"]["subscription_type"], "channel.follow");
    assert_eq!(expected[2]["summary"], "Bob raidet mit 42 Zuschauern.");
    assert_eq!(
        serde_json::from_str::<Value>(expected[4]["data"]["custom"].as_str().unwrap()).unwrap(),
        json!({"value":"äö"})
    );
    assert_eq!(
        expected[7]["summary"],
        "Nach Wiederverbindung folgt dem Kanal."
    );
    assert!(hub.history()["events"].as_array().unwrap().is_empty());
    overlay.stop();
    tokio::time::timeout(Duration::from_secs(3), upstream)
        .await
        .unwrap()
        .unwrap();
}
