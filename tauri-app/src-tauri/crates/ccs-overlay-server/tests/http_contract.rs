use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_overlay_server::{OverlayServer, RealtimeHub, YouTubeMusicBridge};
use serde_json::{json, Value};
use std::sync::Arc;

#[tokio::test]
async fn actual_http_upload_read_delete_and_origin_contract() {
    let dir = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(dir.path().to_path_buf());
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let server = OverlayServer::start(settings, paths, Arc::new(RealtimeHub::new()), 0)
        .await
        .unwrap();
    let base = format!("http://127.0.0.1:{}", server.port);
    let client = reqwest::Client::new();
    let editor = client
        .get(format!("{base}/editor/default"))
        .send()
        .await
        .unwrap();
    assert!(editor.status().is_success());
    assert!(editor.text().await.unwrap().contains("<html"));
    let blocked = client
        .delete(format!("{base}/assets/test"))
        .header("Origin", "https://untrusted.example")
        .send()
        .await
        .unwrap();
    assert_eq!(blocked.status(), 403);
    let form = reqwest::multipart::Form::new().part(
        "file",
        reqwest::multipart::Part::bytes(b"test image".to_vec()).file_name("test.png"),
    );
    let uploaded = client
        .post(format!("{base}/assets"))
        .header("Origin", &base)
        .multipart(form)
        .send()
        .await
        .unwrap();
    assert!(uploaded.status().is_success());
    let asset: Value = uploaded.json().await.unwrap();
    let id = asset["id"].as_str().unwrap();
    let image = client
        .get(format!("{base}/assets/{id}"))
        .send()
        .await
        .unwrap();
    assert_eq!(image.headers()["content-type"], "image/png");
    assert_eq!(image.bytes().await.unwrap().as_ref(), b"test image");
    assert!(client
        .delete(format!("{base}/assets/{id}"))
        .send()
        .await
        .unwrap()
        .status()
        .is_success());
    let listing: Value = client
        .get(format!("{base}/assets"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(listing["assets"], json!([]));
    assert!(!client
        .get(format!("{base}/assets/{id}"))
        .send()
        .await
        .unwrap()
        .status()
        .is_success());
    server.stop();
}

#[tokio::test]
async fn native_music_bridge_roundtrips_browser_state_and_commands_over_http() {
    let bridge = YouTubeMusicBridge::start(0).await.unwrap();
    let base = format!("http://127.0.0.1:{}/ytmusic", bridge.port);
    let client = reqwest::Client::new();
    assert!(bridge.command("next").is_err());
    let response=client.post(format!("{base}/state")).json(&json!({"title":"Track","artist":"Artist","isPlaying":true,"durationMs":5000,"progressMs":1000})).send().await.unwrap();
    assert!(response.status().is_success());
    assert_eq!(bridge.snapshot().title, "Track");
    bridge.command("next").unwrap();
    let commands: Value = client
        .get(format!("{base}/commands"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(commands["commands"], json!(["next"]));
    let empty: Value = client
        .get(format!("{base}/commands"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(empty["commands"], json!([]));
    bridge.stop();
}
