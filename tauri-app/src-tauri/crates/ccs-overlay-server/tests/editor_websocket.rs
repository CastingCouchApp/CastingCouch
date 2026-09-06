use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_overlay_server::{OverlayLayoutStore, OverlayServer, RealtimeHub};
use futures_util::{SinkExt, StreamExt};
use serde_json::{json, Value};
use std::sync::Arc;
use tokio_tungstenite::{connect_async, tungstenite::Message};
#[tokio::test]
async fn editor_frames_persist_and_publish_legacy_layout_envelope() {
    let dir = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(dir.path().to_path_buf());
    let store = OverlayLayoutStore::new(paths.overlay_layouts.clone());
    let server = OverlayServer::start(
        Arc::new(JsonSettingsStore::new(&paths.settings_file)),
        paths,
        Arc::new(RealtimeHub::new()),
        0,
    )
    .await
    .unwrap();
    let (mut ws, _) = connect_async(format!("ws://127.0.0.1:{}/ws", server.port))
        .await
        .unwrap();
    let hello: Value =
        serde_json::from_str(ws.next().await.unwrap().unwrap().to_text().unwrap()).unwrap();
    assert_eq!(hello["type"], "app.ws.hello");
    assert_eq!(hello["data"]["clients"], "1");
    assert_eq!(hello["data"]["overlay.0.id"], "default");
    for (kind, layout) in [
        (
            "editor.layout.set",
            json!({"canvasWidth":1280,"canvasHeight":720,"items":[]}),
        ),
        (
            "editor.layout.patch",
            json!({"canvasWidth":1920,"items":[],"custom":{"keep":true}}),
        ),
    ] {
        ws.send(Message::Text(
            json!({"type":kind,"data":{"instanceId":"contract","layout":layout.to_string()}})
                .to_string()
                .into(),
        ))
        .await
        .unwrap();
        let event = tokio::time::timeout(std::time::Duration::from_secs(2), async {
            loop {
                let event: Value =
                    serde_json::from_str(ws.next().await.unwrap().unwrap().to_text().unwrap())
                        .unwrap();
                if event["type"] == "app.overlay.layout" {
                    break event;
                }
            }
        })
        .await
        .expect("editor change was not published");
        assert_eq!(
            serde_json::from_str::<Value>(event["data"]["layout"].as_str().unwrap()).unwrap(),
            layout
        );
        assert_eq!(store.load("contract").await.unwrap(), layout);
    }
    server.stop();
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        while let Some(Ok(message)) = ws.next().await {
            if matches!(message, Message::Close(_)) {
                break;
            }
        }
    })
    .await
    .expect("server shutdown must close existing browser sockets");
}
