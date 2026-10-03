use ccs_core::store::JsonSettingsStore;
use ccs_modules::{
    music_automation::AlertDucking,
    scene_music::{MusicAction, SceneMusicEngine},
    spotify::{SpotifyClient, SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet},
};
use ccs_secrets::MemorySecretStore;
use serde_json::{json, Value};
use std::{sync::Arc, time::Duration};
use wiremock::{
    matchers::{method, path},
    Mock, MockServer, ResponseTemplate,
};

async fn engine(
    options: Value,
) -> (
    Arc<SceneMusicEngine>,
    Arc<AlertDucking>,
    MockServer,
    tempfile::TempDir,
) {
    let root = tempfile::tempdir().unwrap();
    let settings = Arc::new(JsonSettingsStore::new(root.path().join("settings.json")));
    let mut original = settings.read_value().await.unwrap();
    original["Spotify"]["ClientId"] = json!("client");
    for (key, value) in options.as_object().unwrap() {
        original["Spotify"][key] = value.clone();
    }
    original["Obs"]["LiveScene"] = json!("Game");
    settings
        .save_edit(&settings.read_value().await.unwrap(), &original)
        .await
        .unwrap();
    let server = MockServer::start().await;
    let secrets = Arc::new(MemorySecretStore::new());
    SpotifyTokenRepository::new(secrets.clone())
        .save(&SpotifyTokenSet {
            access_token: "test".into(),
            refresh_token: "refresh".into(),
            obtained_at: chrono::Utc::now(),
            expires_in_seconds: 3600,
            token_type: "Bearer".into(),
            scopes: vec![],
        })
        .unwrap();
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(
                json!({"is_playing":true,"device":{"id":"active","volume_percent":80}}),
            ),
        )
        .mount(&server)
        .await;
    Mock::given(method("GET"))
        .and(path("/me/player/devices"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({"devices":[{"id":"active","is_active":true,"volume_percent":80}]}),
        ))
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .respond_with(ResponseTemplate::new(204))
        .mount(&server)
        .await;
    let player = Arc::new(SpotifyClient::with_http(
        secrets,
        SpotifyOAuthClient::new(),
        server.uri(),
    ));
    let ducking = Arc::new(AlertDucking::new(player.clone()));
    (
        Arc::new(SceneMusicEngine::new(settings, player, ducking.clone())),
        ducking,
        server,
        root,
    )
}
fn scene(name: &str, force: bool) -> MusicAction {
    MusicAction::Scene {
        scene: name.into(),
        force,
    }
}

#[tokio::test]
async fn managed_stream_end_cancels_start_automation_and_prevents_a_second_automatic_pause() {
    let (engine, _, server, _root) =
        engine(json!({"StartOnStreamStart":false,"FadeOutEnabled":false})).await;
    // The observed stream edge is retained while ownership suppresses automatic actions.
    let lease = engine.claim_stream_end();
    assert!(engine.observe_stream(Some(true)).await.is_none());
    lease.mark_stop_started();
    lease.mark_stop_completed();
    drop(lease);
    assert!(engine.observe_stream(Some(false)).await.is_none());
    assert!(server.received_requests().await.unwrap().is_empty());
    assert!(engine.observe_stream(Some(true)).await.is_none());
    engine
        .observe_stream(Some(false))
        .await
        .unwrap()
        .await
        .unwrap()
        .unwrap();
    assert_eq!(writes(&server).await.len(), 1);
}
async fn writes(server: &MockServer) -> Vec<wiremock::Request> {
    server
        .received_requests()
        .await
        .unwrap()
        .into_iter()
        .filter(|r| r.method.as_str() == "PUT")
        .collect()
}

#[tokio::test]
async fn imported_rules_match_case_insensitively_and_start_shuffle_then_volume() {
    let (engine, _, server, _root) = engine(json!({"AutomationRules":[
        {"Name":"Disabled","Enabled":false,"TriggerValue":"Start","ActionType":"Pause"},
        {"Name":"Wrong","TriggerValue":"Elsewhere","ActionType":"Pause"},
        {"Name":"Start music","TriggerType":"obsscenechanged","TriggerValue":"START","ActionType":"StartPlaylist","PlaylistUri":"spotify:playlist:abc","Shuffle":true,"VolumePercent":35,"Custom":{"keep":true}}
    ]})).await;
    engine.run(scene("Start", false)).await.unwrap();
    let requests = writes(&server).await;
    assert_eq!(
        requests.iter().map(|r| r.url.path()).collect::<Vec<_>>(),
        ["/me/player/play", "/me/player/shuffle", "/me/player/volume"]
    );
    assert_eq!(
        requests[0].body_json::<Value>().unwrap(),
        json!({"context_uri":"spotify:playlist:abc"})
    );
    assert!(requests[1]
        .url
        .query_pairs()
        .any(|(k, v)| k == "state" && v == "true"));
    assert!(requests[2]
        .url
        .query_pairs()
        .any(|(k, v)| k == "volume_percent" && v == "35"));
    assert_eq!(engine.status().await["history"][0]["rule"], "Start music");
}

#[tokio::test]
async fn smart_toggle_and_force_and_legacy_live_pause_are_preserved() {
    let (engine, _, server, _root) = engine(json!({"SmartAutomationEnabled":false,"SetVolumeOnLiveTransition":true,"LiveVolumePercent":62,"AutomationRules":[{"TriggerValue":"game","ActionType":"Pause"}]})).await;
    engine.run(scene("Game", false)).await.unwrap();
    assert!(writes(&server).await.is_empty());
    engine.run(scene("Game", true)).await.unwrap();
    let requests = writes(&server).await;
    assert_eq!(requests.len(), 1);
    assert_eq!(requests[0].url.path(), "/me/player/volume");
    assert!(requests[0]
        .url
        .query_pairs()
        .any(|(k, v)| k == "volume_percent" && v == "62"));
}

#[tokio::test]
async fn rule_error_is_visible_and_does_not_skip_following_rule() {
    let (engine, _, server, _root) = engine(json!({"AutomationRules":[{"Name":"Broken","TriggerValue":"Start","ActionType":"StartPlaylist","PlaylistUri":""},{"Name":"Next","TriggerValue":"Start","ActionType":"SetVolume","VolumePercent":24}]})).await;
    assert!(engine.run(scene("Start", false)).await.is_err());
    assert_eq!(writes(&server).await.len(), 1);
    let status = engine.status().await;
    assert!(status["history"]
        .as_array()
        .unwrap()
        .iter()
        .any(|r| r["rule"] == "Broken" && r["success"] == false));
    assert_eq!(status["running"], false);
}

#[tokio::test]
async fn new_scene_cancels_old_delay_before_any_stale_playback() {
    let (engine, _, server, _root) = engine(json!({"AutomationRules":[{"TriggerValue":"Start","ActionType":"StartPlaylist","PlaylistUri":"spotify:playlist:abc","DelaySeconds":2},{"TriggerValue":"End","ActionType":"Pause"}]})).await;
    let old = engine.dispatch(scene("Start", false));
    tokio::time::sleep(Duration::from_millis(50)).await;
    engine.run(scene("End", false)).await.unwrap();
    assert!(old.await.unwrap().is_err());
    assert_eq!(
        writes(&server)
            .await
            .iter()
            .map(|r| r.url.path())
            .collect::<Vec<_>>(),
        ["/me/player/pause"]
    );
}

#[tokio::test]
async fn fade_pins_device_and_pauses_only_after_zero_and_can_be_stopped() {
    let (engine, _, server, _root) = engine(json!({})).await;
    engine
        .run(MusicAction::FadeTo {
            percent: 0,
            milliseconds: 160,
            pause_at_end: true,
        })
        .await
        .unwrap();
    let requests = writes(&server).await;
    assert_eq!(requests.last().unwrap().url.path(), "/me/player/pause");
    assert!(requests.iter().all(|r| r
        .url
        .query_pairs()
        .any(|(k, v)| k == "device_id" && v == "active")));
    assert!(requests[requests.len() - 2]
        .url
        .query_pairs()
        .any(|(k, v)| k == "volume_percent" && v == "0"));
    let fade = engine.dispatch(MusicAction::FadeTo {
        percent: 100,
        milliseconds: 3000,
        pause_at_end: false,
    });
    tokio::time::sleep(Duration::from_millis(50)).await;
    engine.cancel();
    assert!(fade.await.unwrap().is_err());
    let count = writes(&server).await.len();
    tokio::time::sleep(Duration::from_millis(200)).await;
    assert_eq!(writes(&server).await.len(), count);
}

#[tokio::test]
async fn automated_volume_during_alert_changes_restore_target_without_unducking() {
    let (engine, ducking, server, _root)=engine(json!({"AutomationRules":[{"TriggerValue":"Game","ActionType":"Resume","VolumePercent":90,"FadeEnabled":true,"FadeMilliseconds":160}]})).await;
    ducking
        .begin(
            "alert",
            "client",
            &json!({"AlertMuteVolumePercent":20,"FadeDuringAlerts":false}),
        )
        .await
        .unwrap();
    engine.run(scene("Game", false)).await.unwrap();
    let requests = writes(&server).await;
    assert_eq!(
        requests
            .iter()
            .filter(|r| r.url.path() == "/me/player/volume")
            .count(),
        1
    );
    ducking.end("alert", false).await.unwrap();
    assert!(writes(&server)
        .await
        .last()
        .unwrap()
        .url
        .query_pairs()
        .any(|(k, v)| k == "volume_percent" && v == "90"));
}

#[tokio::test]
async fn stream_observation_deduplicates_across_connection_gaps_and_pauses_on_end() {
    let (engine, _, server, _root)=engine(json!({"StartPlaylistUri":"spotify:playlist:start","StartVolumePercent":48,"FadeInEnabled":false,"FadeOutEnabled":false})).await;
    engine.observe_stream(Some(false)).await;
    engine
        .observe_stream(Some(true))
        .await
        .unwrap()
        .await
        .unwrap()
        .unwrap();
    assert!(engine.observe_stream(Some(true)).await.is_none());
    assert!(engine.observe_stream(None).await.is_none());
    assert!(engine.observe_stream(Some(true)).await.is_none());
    engine
        .observe_stream(Some(false))
        .await
        .unwrap()
        .await
        .unwrap()
        .unwrap();
    let requests = writes(&server).await;
    assert_eq!(
        requests.iter().map(|r| r.url.path()).collect::<Vec<_>>(),
        [
            "/me/player/play",
            "/me/player/volume",
            "/me/player/shuffle",
            "/me/player/pause"
        ]
    );
    assert_eq!(
        requests
            .iter()
            .filter(|r| r.url.path() == "/me/player/play")
            .count(),
        1
    );
    assert_eq!(requests.last().unwrap().url.path(), "/me/player/pause");
    assert!(requests.iter().any(|r| r
        .url
        .query_pairs()
        .any(|(k, v)| k == "volume_percent" && v == "48")));
}

#[tokio::test]
async fn invalid_fade_is_rejected_without_network_and_end_scene_music_uses_native_flag() {
    let (engine, _, server, root)=engine(json!({"StartPlaylistUri":"spotify:playlist:start","PlayEndMusic":true,"AutomationRules":[],"SmartAutomationEnabled":false})).await;
    assert!(engine
        .run(MusicAction::FadeTo {
            percent: 101,
            milliseconds: 0,
            pause_at_end: false
        })
        .await
        .is_err());
    assert!(server.received_requests().await.unwrap().is_empty());
    let settings = JsonSettingsStore::new(root.path().join("settings.json"));
    let original = settings.read_value().await.unwrap();
    let mut next = original.clone();
    next["Obs"]["EndScene"] = json!("Outro");
    settings.save_edit(&original, &next).await.unwrap();
    engine.run(scene("Outro", false)).await.unwrap();
    assert_eq!(
        writes(&server).await[0].body_json::<Value>().unwrap()["context_uri"],
        "spotify:playlist:start"
    );
}

#[tokio::test]
async fn actual_obs_scene_events_run_rules_once_despite_snapshot_refresh_and_duplicate_events() {
    use ccs_modules::obs::ObsClient;
    use futures_util::{SinkExt, StreamExt};
    use tokio_tungstenite::tungstenite::Message;
    let (engine,_,server,_root)=engine(json!({"AutomationRules":[{"TriggerValue":"Game","ActionType":"Resume","VolumePercent":35}]})).await;
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let port = listener.local_addr().unwrap().port();
    let peer = tokio::spawn(async move {
        let (socket, _) = listener.accept().await.unwrap();
        let mut ws = tokio_tungstenite::accept_async(socket).await.unwrap();
        ws.send(Message::Text(
            json!({"op":0,"d":{"obsWebSocketVersion":"5.6.0","rpcVersion":1}})
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
        let mut scene = "Idle".to_string();
        while let Some(Ok(Message::Text(text))) = ws.next().await {
            let req: Value = serde_json::from_str(&text).unwrap();
            let d = &req["d"];
            if d["requestType"] == "SetCurrentProgramScene" {
                scene = d["requestData"]["sceneName"].as_str().unwrap().into();
                ws.send(Message::Text(json!({"op":5,"d":{"eventType":"CurrentProgramSceneChanged","eventIntent":4,"eventData":{"sceneName":scene}}}).to_string().into())).await.unwrap();
            }
            let data = if d["requestType"] == "GetSceneList" {
                json!({"currentProgramSceneName":scene,"scenes":[{"sceneName":scene,"sceneIndex":0}]})
            } else {
                json!({})
            };
            ws.send(Message::Text(json!({"op":7,"d":{"requestType":d["requestType"],"requestId":d["requestId"],"requestStatus":{"result":true,"code":100},"responseData":data}}).to_string().into())).await.unwrap();
        }
    });
    let obs = ObsClient::new_shared("127.0.0.1", port);
    let binding = tokio::spawn(engine.bind_obs(&obs));
    obs.connect_simple("127.0.0.1", port, None, false)
        .await
        .unwrap();
    obs.set_current_program_scene("Game").await.unwrap();
    tokio::time::timeout(Duration::from_secs(3), async {
        while writes(&server).await.len() < 2 {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    obs.get_scene_list().await.unwrap();
    obs.get_scene_list().await.unwrap();
    tokio::time::sleep(Duration::from_millis(100)).await;
    assert_eq!(
        writes(&server)
            .await
            .iter()
            .filter(|r| r.url.path() == "/me/player/play")
            .count(),
        1
    );
    obs.disconnect().await.unwrap();
    peer.await.unwrap();
    binding.abort();
}

#[tokio::test]
async fn legacy_music_flags_are_honored_and_explicit_native_flags_win() {
    let (engine,_,server,root)=engine(json!({"StartPlaylistUri":"spotify:playlist:abc","FadeInEnabled":false,"StartOnStreamStart":true})).await;
    let store = JsonSettingsStore::new(root.path().join("settings.json"));
    let original = store.read_value().await.unwrap();
    let mut next = original.clone();
    next["Workflow"] = json!({"AutoStartSpotifyPlaylist":false,"PauseSpotifyOnStreamEnd":false});
    store.save_edit(&original, &next).await.unwrap();
    engine.run(MusicAction::StreamStarted).await.unwrap();
    engine.run(MusicAction::StreamStopped).await.unwrap();
    assert!(!writes(&server)
        .await
        .iter()
        .any(|r| r.url.path() == "/me/player/pause"));
    let original = store.read_value().await.unwrap();
    let mut next = original.clone();
    next["Spotify"]["StartOnStreamStart"] = Value::Null;
    store.save_edit(&original, &next).await.unwrap();
    let count = writes(&server).await.len();
    engine.run(MusicAction::StreamStarted).await.unwrap();
    assert_eq!(writes(&server).await.len(), count);
}

#[tokio::test]
async fn disabled_stream_music_does_not_cancel_a_scene_rule_and_close_blocks_late_actions() {
    let (engine,_,server,_root)=engine(json!({"StartOnStreamStart":false,"AutomationRules":[{"TriggerValue":"Intro","ActionType":"Pause","DelaySeconds":1}]})).await;
    let pending = engine.dispatch(scene("Intro", false));
    tokio::time::sleep(Duration::from_millis(20)).await;
    assert!(engine.observe_stream(Some(true)).await.is_none());
    pending.await.unwrap().unwrap();
    assert_eq!(writes(&server).await.len(), 1);
    engine.close().await;
    assert!(engine.run(scene("Intro", true)).await.is_err());
    assert!(engine.observe_stream(Some(false)).await.is_none());
    assert_eq!(writes(&server).await.len(), 1);
}

#[tokio::test]
async fn stream_start_retries_a_failed_request_and_fade_targets_preferred_device_volume() {
    use std::sync::atomic::{AtomicUsize, Ordering};
    let (engine,_,server,_root)=engine(json!({"StartPlaylistUri":"spotify:playlist:start","FadeInEnabled":false,"AutoTransferToPreferredDevice":false,"PreferredDeviceId":"preferred"})).await;
    let attempts = Arc::new(AtomicUsize::new(0));
    let count = attempts.clone();
    Mock::given(method("PUT"))
        .and(path("/me/player/play"))
        .respond_with(move |_: &wiremock::Request| {
            if count.fetch_add(1, Ordering::SeqCst) == 0 {
                ResponseTemplate::new(503)
            } else {
                ResponseTemplate::new(204)
            }
        })
        .with_priority(1)
        .expect(2)
        .mount(&server)
        .await;
    engine.run(MusicAction::StreamStarted).await.unwrap();
    assert_eq!(attempts.load(Ordering::SeqCst), 2);
    assert!(engine.status().await["history"]
        .as_array()
        .unwrap()
        .iter()
        .any(|r| r["success"] == false));
    Mock::given(method("GET"))
        .and(path("/me/player/devices"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({"devices":[{"id":"preferred","is_active":false,"volume_percent":60}]}),
        ))
        .with_priority(1)
        .mount(&server)
        .await;
    engine
        .run(MusicAction::FadeTo {
            percent: 30,
            milliseconds: 150,
            pause_at_end: false,
        })
        .await
        .unwrap();
    let requests = writes(&server).await;
    let volumes = requests
        .iter()
        .filter(|r| r.url.path() == "/me/player/volume")
        .collect::<Vec<_>>();
    assert!(volumes
        .last()
        .unwrap()
        .url
        .query_pairs()
        .any(|(k, v)| k == "device_id" && v == "preferred"));
    assert!(volumes
        .last()
        .unwrap()
        .url
        .query_pairs()
        .any(|(k, v)| k == "volume_percent" && v == "30"));
}
