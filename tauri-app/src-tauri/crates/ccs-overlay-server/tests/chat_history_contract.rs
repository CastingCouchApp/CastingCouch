use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_overlay_server::{OverlayServer, RealtimeHub};
use futures_util::{SinkExt, StreamExt};
use serde_json::{json, Value};
use std::sync::Arc;

async fn receive(
    ws: &mut tokio_tungstenite::WebSocketStream<
        tokio_tungstenite::MaybeTlsStream<tokio::net::TcpStream>,
    >,
) -> Value {
    let event = tokio::time::timeout(std::time::Duration::from_secs(3), ws.next())
        .await
        .expect("WebSocket response timed out")
        .unwrap()
        .unwrap();
    serde_json::from_str(event.to_text().unwrap()).unwrap()
}

fn message(index: usize) -> Value {
    json!({"source":"twitch","type":"channel.chat.message","at":"2026-10-03T10:00:00Z","data":{"messageId":format!("m{index}"),"userId":"user","parts":"[]"}})
}

#[tokio::test]
async fn csharp_history_capacity_tracks_http_websocket_layouts_and_restart() {
    let dir = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(dir.path().into());
    std::fs::create_dir_all(&paths.overlay_layouts).unwrap();
    // Include a layout outside the selected canvas, string maxLines and case-insensitive type.
    std::fs::write(
        paths.overlay_layouts.join("other.json"),
        json!({"items":[{"type":"CHAT","props":{"maxLines":"750"}}]}).to_string(),
    )
    .unwrap();
    let original: Vec<_> = (0..1600).map(message).collect();
    std::fs::write(
        paths.overlay_root.join("chat-history.json"),
        json!(original).to_string(),
    )
    .unwrap();
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let hub = Arc::new(RealtimeHub::new());
    let server = OverlayServer::start(settings.clone(), paths.clone(), hub.clone(), 0)
        .await
        .unwrap();
    let base = format!("http://127.0.0.1:{}", server.port);
    let http = reqwest::Client::new();
    let config: Value = http
        .get(format!("{base}/chat/config"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(config["maxBufferedMessages"], 1500);
    let history: Value = http
        .get(format!("{base}/chat/history"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(history["events"].as_array().unwrap().len(), 1500);
    assert_eq!(history["events"][0]["data"]["messageId"], "m100");
    let (mut ws, _) =
        tokio_tungstenite::connect_async(format!("ws://127.0.0.1:{}/ws", server.port))
            .await
            .unwrap();
    assert_eq!(receive(&mut ws).await["type"], "app.ws.hello");
    assert_eq!(receive(&mut ws).await["type"], "app.countdown");
    for index in 100..1600 {
        let event = receive(&mut ws).await;
        assert_eq!(event, message(index));
    }
    // HTTP edits must immediately change the common buffer, not merely its response limit.
    http.put(format!("{base}/layout/other"))
        .json(&json!({"items":[{"type":"chat","props":{"maxLines":120}}]}))
        .send()
        .await
        .unwrap()
        .error_for_status()
        .unwrap();
    let config: Value = http
        .get(format!("{base}/chat/config"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(config["maxBufferedMessages"], 240);
    assert_eq!(hub.history()["events"].as_array().unwrap().len(), 240);
    // The editor WebSocket write path uses the same capacity calculation.
    ws.send(tokio_tungstenite::tungstenite::Message::Text(json!({"type":"editor.layout.set","data":{"instanceId":"other","layout":{"items":[{"type":"chat","props":{"maxLines":1000}}]}}}).to_string().into())).await.unwrap();
    loop {
        let event = receive(&mut ws).await;
        if event["type"] == "app.overlay.layout"
            && event["data"]["layout"]
                .as_str()
                .is_some_and(|s| s.contains("1000"))
        {
            break;
        }
    }
    for index in 1600..3800 {
        hub.publish(&message(index));
    }
    assert_eq!(hub.history()["events"].as_array().unwrap().len(), 2000);
    hub.publish(&json!({"source":"twitch","type":"channel.chat.message_delete","data":{"message_id":"m3799"}}));
    hub.flush_history().unwrap();
    let file: Value = serde_json::from_slice(
        &std::fs::read(paths.overlay_root.join("chat-history.json")).unwrap(),
    )
    .unwrap();
    assert_eq!(file.as_array().unwrap().len(), 1999);
    ws.close(None).await.unwrap();
    server.stop();
    let restored = Arc::new(RealtimeHub::new());
    let second = OverlayServer::start(settings, paths, restored.clone(), 0)
        .await
        .unwrap();
    assert_eq!(restored.history()["events"].as_array().unwrap().len(), 1999);
    assert_eq!(
        restored.history()["events"][0]["data"]["messageId"],
        "m1800"
    );
    assert_eq!(
        restored.history()["events"]
            .as_array()
            .unwrap()
            .last()
            .unwrap()["data"]["messageId"],
        "m3798"
    );
    second.stop();
}

#[tokio::test]
async fn configured_csharp_history_root_is_used_and_disabled_overlay_keeps_the_buffer() {
    let dir = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(dir.path().into());
    let imported = dir.path().join("ImportedOverlay");
    std::fs::create_dir_all(&imported).unwrap();
    std::fs::write(
        imported.join("chat-history.json"),
        json!([message(1)]).to_string(),
    )
    .unwrap();
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let mut saved = settings.load().await.unwrap();
    saved.overlay.root_path = format!(" {} ", imported.display());
    saved.overlay.chat.enabled = false;
    settings.save(&saved).await.unwrap();
    let hub = Arc::new(RealtimeHub::new());
    let server = OverlayServer::start(settings.clone(), paths, hub.clone(), 0)
        .await
        .unwrap();
    assert_eq!(hub.history()["events"], json!([message(1)]));
    let base = format!("http://127.0.0.1:{}", server.port);
    let history: Value = reqwest::get(format!("{base}/chat/history"))
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(history["events"], json!([]));
    saved.overlay.chat.enabled = true;
    settings.save(&saved).await.unwrap();
    let config: Value = reqwest::get(format!("{base}/chat/config"))
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(config["maxBufferedMessages"], 160);
    let history: Value = reqwest::get(format!("{base}/chat/history"))
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(history["events"], json!([message(1)]));
    hub.publish(&message(2));
    hub.flush_history().unwrap();
    let file: Value =
        serde_json::from_slice(&std::fs::read(imported.join("chat-history.json")).unwrap())
            .unwrap();
    assert_eq!(file.as_array().unwrap().len(), 2);
    server.stop();
}
