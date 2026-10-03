use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_overlay_server::{OverlayServer, RealtimeHub, YouTubeMusicBridge};
use serde_json::{json, Value};
use std::sync::Arc;

#[tokio::test]
async fn chat_appearance_normalizes_csharp_values_without_rewriting_settings() {
    let dir = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(dir.path().into());
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let image = dir.path().join("background.png");
    std::fs::write(&image, b"image-fixture").unwrap();
    let mut saved = settings.load().await.unwrap();
    saved.overlay.chat.extra = json!({
        "BackgroundType":"  cOlOr  ","BackgroundImagePath":format!(" {} ", image.display()),
        "BackgroundColor":"   ","BackgroundOpacity":2,"PaddingPx":-1,
        "BorderRadiusPx":999,"GapPx":null,"FontSizePx":1,"FontFamily":"  ",
        "":true,"ÄltererWert":{"keep":true}
    });
    settings.save(&saved).await.unwrap();
    let original = settings.read_value().await.unwrap();
    let server = OverlayServer::start(settings.clone(), paths, Arc::new(RealtimeHub::new()), 0)
        .await
        .unwrap();
    let base = format!("http://127.0.0.1:{}", server.port);
    let http = reqwest::Client::new();
    let config: Value = http
        .get(format!("{base}/chat/config"))
        .send()
        .await
        .unwrap()
        .error_for_status()
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(config["backgroundType"], "Color");
    assert_eq!(config["backgroundVersion"], "0");
    assert_eq!(config["backgroundColor"], "#000000");
    assert_eq!(config["backgroundOpacity"], 1.0);
    assert_eq!(config["paddingPx"], 0);
    assert_eq!(config["borderRadiusPx"], 64);
    assert_eq!(config["gapPx"], 6);
    assert_eq!(config["fontSizePx"], 8);
    assert_eq!(config["fontFamily"], "Segoe UI, system-ui, sans-serif");
    assert!(config.get("backgroundImagePath").is_none());
    assert_eq!(
        http.get(format!("{base}/chat/background"))
            .send()
            .await
            .unwrap()
            .status(),
        404
    );
    assert_eq!(settings.read_value().await.unwrap(), original);
    saved.overlay.chat.extra["BackgroundType"] = json!(" image ");
    saved.overlay.chat.extra["FontFamily"] = json!("  Arial  ");
    saved.overlay.chat.extra["BackgroundColor"] = json!("  #123456  ");
    settings.save(&saved).await.unwrap();
    let config: Value = http
        .get(format!("{base}/chat/config"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(config["backgroundType"], "Image");
    assert_ne!(config["backgroundVersion"], "0");
    assert_eq!(config["fontFamily"], "Arial");
    assert_eq!(config["backgroundColor"], "#123456");
    let response = http
        .get(format!("{base}/chat/background"))
        .send()
        .await
        .unwrap();
    assert_eq!(response.headers()["content-type"], "image/png");
    assert_eq!(response.bytes().await.unwrap().as_ref(), b"image-fixture");
    std::fs::remove_file(image).unwrap();
    let config: Value = http
        .get(format!("{base}/chat/config"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(config["backgroundType"], "None");
    assert_eq!(config["backgroundVersion"], "0");
    server.stop();
}

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
    let layout = json!({"canvasWidth":1280,"canvasHeight":720,"items":[],"custom":true});
    let written = client
        .put(format!("{base}/layout/roundtrip"))
        .json(&layout)
        .send()
        .await
        .unwrap();
    assert_eq!(written.status(), 200);
    assert_eq!(written.json::<Value>().await.unwrap(), layout);
    for invalid in [json!([]), json!(null)] {
        assert_eq!(
            client
                .put(format!("{base}/layout/roundtrip"))
                .json(&invalid)
                .send()
                .await
                .unwrap()
                .status(),
            400
        );
    }
    assert_eq!(
        client
            .get(format!("{base}/layout/invalid%20id"))
            .send()
            .await
            .unwrap()
            .status(),
        400
    );

    let health: Value = client
        .get(format!("{base}/health"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(health["port"], server.port);
    let redirect = client.get(format!("{base}/editor")).send().await.unwrap();
    assert_eq!(redirect.url().path(), "/editor/default");

    let missing: Value = client
        .get(format!("{base}/layout/missing"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(missing["canvasWidth"], 1920);
    assert_eq!(missing["canvasHeight"], 1080);

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

#[tokio::test]
async fn chat_configuration_and_disabled_history_follow_csharp_contract() {
    let dir = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(dir.path().into());
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let mut config = settings.load().await.unwrap();
    config.overlay.chat.enabled = false;
    config.overlay.chat.extra =
        json!({"BackgroundType":"Image","BackgroundImagePath":"absent.png"});
    settings.save(&config).await.unwrap();
    let hub = Arc::new(RealtimeHub::new());
    hub.publish(&json!({"type":"channel.chat.message","data":{"messageId":"test"}}));
    let server = OverlayServer::start(settings, paths, hub, 0).await.unwrap();
    let client = reqwest::Client::new();
    let base = format!("http://127.0.0.1:{}", server.port);
    let response = client
        .get(format!("{base}/chat/config"))
        .send()
        .await
        .unwrap();
    let config: Value = response.json().await.unwrap();
    assert_eq!(config["backgroundType"], "None");
    assert_eq!(config["backgroundOpacity"], 0.55);
    assert_eq!(config["backgroundVersion"], "0");
    assert_eq!(config["fontSizePx"], 18);
    let history: Value = client
        .get(format!("{base}/chat/history"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(history["events"], json!([]));
    server.stop();
}
