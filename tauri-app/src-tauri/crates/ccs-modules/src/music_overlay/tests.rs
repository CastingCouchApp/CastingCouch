use super::*;
use crate::obs::ObsClient;
use futures_util::{SinkExt, StreamExt};
use serde_json::json;
use std::sync::atomic::{AtomicBool, Ordering};
use tokio_tungstenite::tungstenite::Message;

fn playing() -> MusicPlayerSnapshot {
    MusicPlayerSnapshot {
        provider: "spotify".into(),
        connected: true,
        is_playing: true,
        title: "Song".into(),
        cover_url: "cover".into(),
        volume_percent: Some(50),
        ..Default::default()
    }
}

#[test]
fn csharp_policy_uses_legacy_spotify_flags_pause_grace_and_complete_metadata() {
    let mut settings = AppSettings::default();
    settings.spotify.extra = json!({"OverlayHideWhenPaused":true,"OverlayHideWhenMuted":true,"OverlayShowTitle":false,"OverlayShowProgress":false});
    settings.music_player.extra = json!({"ShowInOverlay":false,"ShowTitle":false});
    let config = OverlayConfig::from_settings(&settings);
    let start = Instant::now();
    let mut state = VisibilityState::default();
    let mut song = playing();
    let data = state.payload(&config, &song, start);
    assert_eq!(data["visible"], true);
    assert_eq!(data["showTitle"], true);
    assert_eq!(data["showProgress"], true);
    assert_eq!(data["cover"], "cover");
    song.is_playing = false;
    assert_eq!(
        state.payload(&config, &song, start + Duration::from_secs(2))["visible"],
        true
    );
    assert_eq!(
        state.payload(&config, &song, start + Duration::from_secs(3))["visible"],
        false
    );
    song.is_playing = true;
    assert_eq!(
        state.payload(&config, &song, start + Duration::from_secs(4))["visible"],
        true
    );
    song.volume_percent = Some(0);
    assert_eq!(
        state.payload(&config, &song, start + Duration::from_secs(5))["visible"],
        false
    );
    settings.spotify.extra["SmartAutomationEnabled"] = json!(false);
    assert_eq!(
        state.payload(
            &OverlayConfig::from_settings(&settings),
            &song,
            start + Duration::from_secs(6)
        )["visible"],
        true
    );
    song.connected = false;
    assert_eq!(
        state.payload(
            &OverlayConfig::from_settings(&settings),
            &song,
            start + Duration::from_secs(6)
        )["visible"],
        false
    );
}

#[test]
fn independent_detectors_unknown_volume_and_audio_source_changes_do_not_invent_a_mute() {
    let mut settings = AppSettings::default();
    let start = Instant::now();
    let mut state = VisibilityState::default();
    let mut song = playing();
    song.volume_percent = None;
    assert_eq!(
        state.payload(&OverlayConfig::from_settings(&settings), &song, start)["visible"],
        true
    );
    state.obs_mute = Some(("Spotify".into(), true));
    assert_eq!(
        state.payload(&OverlayConfig::from_settings(&settings), &song, start)["visible"],
        false
    );
    settings.spotify.extra = json!({"OverlayMuteDetectionObsSource":false});
    assert_eq!(
        state.payload(&OverlayConfig::from_settings(&settings), &song, start)["visible"],
        true
    );
    settings.spotify.extra = json!({"OverlayObsAudioSource":"Other"});
    assert_eq!(
        state.payload(&OverlayConfig::from_settings(&settings), &song, start)["visible"],
        true
    );
    song.provider = "ytmusic".into();
    song.volume_percent = None;
    let data = state.payload(&OverlayConfig::from_settings(&settings), &song, start);
    assert_eq!(data["visible"], true);
    assert_eq!(data["hideWhenMuted"], true);
    settings.spotify.extra = json!({"OverlayEnabled":false});
    assert_eq!(
        state.payload(&OverlayConfig::from_settings(&settings), &song, start)["overlayEnabled"],
        false
    );
}

async fn obs_server() -> (
    Arc<ObsClient>,
    Arc<AtomicBool>,
    Arc<AtomicBool>,
    Arc<AtomicBool>,
    Arc<std::sync::Mutex<Vec<Value>>>,
    tokio::task::JoinHandle<()>,
    u16,
) {
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let port = listener.local_addr().unwrap().port();
    let muted = Arc::new(AtomicBool::new(true));
    let fail_mute = Arc::new(AtomicBool::new(false));
    let fail_set = Arc::new(AtomicBool::new(false));
    let requests = Arc::new(std::sync::Mutex::new(vec![]));
    let (m, f, s, r) = (
        muted.clone(),
        fail_mute.clone(),
        fail_set.clone(),
        requests.clone(),
    );
    let task = tokio::spawn(async move {
        loop {
            let Ok((socket, _)) = listener.accept().await else {
                break;
            };
            let mut ws = tokio_tungstenite::accept_async(socket).await.unwrap();
            ws.send(Message::Text(
                json!({"op":0,"d":{"rpcVersion":1,"obsWebSocketVersion":"5.6.0"}})
                    .to_string()
                    .into(),
            ))
            .await
            .unwrap();
            ws.next().await.unwrap().unwrap();
            ws.send(Message::Text(
                json!({"op":2,"d":{"negotiatedRpcVersion":1}})
                    .to_string()
                    .into(),
            ))
            .await
            .unwrap();
            while let Some(Ok(Message::Text(text))) = ws.next().await {
                let req: Value = serde_json::from_str(&text).unwrap();
                let d = &req["d"];
                let kind = d["requestType"].as_str().unwrap();
                r.lock().unwrap().push(d.clone());
                let failed = (kind == "GetInputMute" && f.load(Ordering::SeqCst))
                    || (kind == "SetSceneItemEnabled" && s.load(Ordering::SeqCst));
                let data = match kind {
                    "GetInputMute" => json!({"inputMuted":m.load(Ordering::SeqCst)}),
                    "GetSceneItemId" => json!({"sceneItemId":17}),
                    _ => json!({}),
                };
                if ws.send(Message::Text(json!({"op":7,"d":{"requestId":d["requestId"],"requestType":kind,"requestStatus":{"result":!failed,"code":if failed{500}else{100},"comment":if failed{"contract failure"}else{""}},"responseData":data}}).to_string().into())).await.is_err() {break;}
            }
        }
    });
    let obs = ObsClient::new_shared("127.0.0.1", port);
    obs.connect_simple("127.0.0.1", port, None, false)
        .await
        .unwrap();
    (obs, muted, fail_mute, fail_set, requests, task, port)
}

#[tokio::test]
async fn real_obs_mute_failures_retries_and_reconnect_preserve_json_visibility() {
    let root = tempfile::tempdir().unwrap();
    let settings = Arc::new(JsonSettingsStore::new(root.path().join("settings.json")));
    let mut config = AppSettings::default();
    config.spotify.extra = json!({"OverlayObsScene":"Live","OverlayObsSource":"Music Browser"});
    settings.save(&config).await.unwrap();
    let (obs, muted, fail_mute, fail_set, requests, task, port) = obs_server().await;
    let runtime = MusicOverlayRuntime::new(settings.clone(), obs.clone());
    runtime.snapshot(&playing(), &config);
    runtime.tick().await.unwrap();
    assert_eq!(runtime.snapshot(&playing(), &config)["visible"], false);
    let sets = || {
        requests
            .lock()
            .unwrap()
            .iter()
            .filter(|r| r["requestType"] == "SetSceneItemEnabled")
            .cloned()
            .collect::<Vec<_>>()
    };
    assert_eq!(
        sets()[0]["requestData"],
        json!({"sceneName":"Live","sceneItemId":17,"sceneItemEnabled":false})
    );
    runtime.tick().await.unwrap();
    assert_eq!(sets().len(), 1, "do not rewrite unchanged visibility");
    fail_mute.store(true, Ordering::SeqCst);
    muted.store(false, Ordering::SeqCst);
    assert!(runtime.tick().await.is_err());
    let data = runtime.snapshot(&playing(), &config);
    assert_eq!(data["visible"], false);
    assert!(data["overlayError"]
        .as_str()
        .unwrap()
        .contains("contract failure"));
    assert_eq!(sets().len(), 1);
    fail_mute.store(false, Ordering::SeqCst);
    fail_set.store(true, Ordering::SeqCst);
    assert!(runtime.tick().await.is_err());
    assert_eq!(runtime.snapshot(&playing(), &config)["visible"], true);
    fail_set.store(false, Ordering::SeqCst);
    runtime.tick().await.unwrap();
    assert_eq!(
        sets().last().unwrap()["requestData"]["sceneItemEnabled"],
        true
    );
    assert_eq!(
        runtime.snapshot(&playing(), &config)["overlayError"],
        Value::Null
    );
    let before = sets().len();
    obs.disconnect().await.unwrap();
    runtime.tick().await.unwrap();
    obs.connect_simple("127.0.0.1", port, None, false)
        .await
        .unwrap();
    runtime.tick().await.unwrap();
    assert_eq!(sets().len(), before + 1);
    config.spotify.extra["OverlayObsSource"] = json!("Another Browser");
    settings.save(&config).await.unwrap();
    runtime.snapshot(&playing(), &config);
    runtime.tick().await.unwrap();
    assert_eq!(
        requests
            .lock()
            .unwrap()
            .iter()
            .rev()
            .find(|r| r["requestType"] == "GetSceneItemId")
            .unwrap()["requestData"]["sourceName"],
        "Another Browser"
    );
    obs.disconnect().await.unwrap();
    task.abort();
}
