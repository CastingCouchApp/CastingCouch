use ccs_core::{spotify_states::SpotifyStateStore, store::JsonSettingsStore};
use ccs_modules::{
    music_automation::AlertDucking,
    scene_music::{MusicAction, SceneMusicEngine},
    spotify::{SpotifyClient, SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet},
    spotify_states::{MusicStateAction, SpotifyStateRuntime},
};
use ccs_secrets::MemorySecretStore;
use serde_json::{json, Value};
use std::{sync::Arc, time::Duration};
use wiremock::{
    matchers::{method, path},
    Mock, MockServer, ResponseTemplate,
};
async fn setup() -> (
    Arc<SpotifyStateRuntime>,
    Arc<SceneMusicEngine>,
    Arc<AlertDucking>,
    MockServer,
    tempfile::TempDir,
    Arc<SpotifyClient>,
) {
    let root = tempfile::tempdir().unwrap();
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
    let player = Arc::new(SpotifyClient::with_http(
        secrets,
        SpotifyOAuthClient::new(),
        server.uri(),
    ));
    let settings = Arc::new(JsonSettingsStore::new(root.path().join("settings.json")));
    let original = settings.read_value().await.unwrap();
    let mut next = original.clone();
    next["Spotify"]["ClientId"] = json!("client");
    next["Spotify"]["PreferredDeviceId"] = json!("device");
    settings.save_edit(&original, &next).await.unwrap();
    Mock::given(method("GET")).and(path("/me/player")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"is_playing":false,"shuffle_state":true,"repeat_state":"context","progress_ms":2400,"context":{"uri":"spotify:playlist:list"},"device":{"id":"device","volume_percent":64},"item":{"id":"song","uri":"spotify:track:song","type":"track","name":"Song","duration_ms":90000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[{"url":"cover"}]}}}))).mount(&server).await;
    Mock::given(method("GET"))
        .and(path("/me/player/devices"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({"devices":[{"id":"device","is_active":true,"volume_percent":64}]}),
        ))
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .respond_with(ResponseTemplate::new(204))
        .mount(&server)
        .await;
    let ducking = Arc::new(AlertDucking::new(player.clone()));
    let engine = Arc::new(SceneMusicEngine::new(
        settings.clone(),
        player.clone(),
        ducking.clone(),
    ));
    let runtime = Arc::new(SpotifyStateRuntime::new(
        Arc::new(SpotifyStateStore::new(root.path())),
        settings,
        player.clone(),
        engine.clone(),
        ducking.clone(),
    ));
    (runtime, engine, ducking, server, root, player)
}
#[tokio::test]
async fn capture_and_restore_replay_context_track_position_options_and_paused_state() {
    let (runtime, _, _, server, root, _player) = setup().await;
    runtime
        .action(MusicStateAction::Capture {
            group: "Intro".into(),
        })
        .await
        .unwrap();
    let persisted = SpotifyStateStore::new(root.path())
        .get("intro")
        .await
        .unwrap();
    assert_eq!(persisted.track["Name"], "Song");
    assert_eq!(persisted.volume_percent, 64);
    runtime
        .action(MusicStateAction::Restore {
            group: "Intro".into(),
            fade_seconds: 0,
        })
        .await
        .unwrap();
    let requests = server
        .received_requests()
        .await
        .unwrap()
        .into_iter()
        .filter(|r| r.method.as_str() == "PUT")
        .collect::<Vec<_>>();
    assert_eq!(
        requests.iter().map(|r| r.url.path()).collect::<Vec<_>>(),
        [
            "/me/player/repeat",
            "/me/player/play",
            "/me/player/shuffle",
            "/me/player/seek",
            "/me/player/volume",
            "/me/player/pause"
        ]
    );
    assert_eq!(
        requests[1].body_json::<Value>().unwrap(),
        json!({"context_uri":"spotify:playlist:list","offset":{"uri":"spotify:track:song"}})
    );
    assert!(requests.iter().all(|r| r
        .url
        .query_pairs()
        .any(|(k, v)| k == "device_id" && v == "device")));
    assert!(runtime.snapshot().await.unwrap()["states"]
        .as_object()
        .unwrap()
        .is_empty());
    assert_eq!(
        runtime.snapshot().await.unwrap()["history"]["RestoredCount"],
        1
    );
}
#[tokio::test]
async fn failed_seek_and_cancelled_restore_keep_the_original_snapshot() {
    let (runtime, engine, _, server, _root, _player) = setup().await;
    runtime
        .action(MusicStateAction::Capture {
            group: "Intro".into(),
        })
        .await
        .unwrap();
    Mock::given(method("PUT"))
        .and(path("/me/player/seek"))
        .respond_with(ResponseTemplate::new(500))
        .with_priority(1)
        .mount(&server)
        .await;
    assert!(runtime
        .action(MusicStateAction::Restore {
            group: "Intro".into(),
            fade_seconds: 0
        })
        .await
        .is_err());
    assert!(runtime.snapshot().await.unwrap()["states"]
        .get("Intro")
        .is_some());
    let running = runtime.clone();
    let task = tokio::spawn(async move {
        running
            .action(MusicStateAction::Restore {
                group: "Intro".into(),
                fade_seconds: 3,
            })
            .await
    });
    tokio::time::sleep(Duration::from_millis(50)).await;
    engine.run(MusicAction::Stop).await.unwrap();
    assert!(task.await.unwrap().is_err());
    assert!(runtime.snapshot().await.unwrap()["states"]
        .get("Intro")
        .is_some());
}
#[tokio::test]
async fn capture_during_ducking_stores_desired_volume_and_restore_does_not_unduck() {
    let (runtime, _, ducking, server, _root, _player) = setup().await;
    Mock::given(method("GET")).and(path("/me/player")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"is_playing":true,"progress_ms":0,"repeat_state":"off","device":{"id":"device","volume_percent":64},"item":{"id":"song","uri":"spotify:track:song","type":"track","name":"Song"}}))).with_priority(1).mount(&server).await;
    ducking
        .begin(
            "alert",
            "client",
            &json!({"AlertMuteVolumePercent":20,"FadeDuringAlerts":false}),
        )
        .await
        .unwrap();
    ducking.set_volume("client", 82).await.unwrap();
    runtime
        .action(MusicStateAction::Capture {
            group: "Intro".into(),
        })
        .await
        .unwrap();
    assert_eq!(
        runtime.snapshot().await.unwrap()["states"]["Intro"]["VolumePercent"],
        82
    );
    runtime
        .action(MusicStateAction::Restore {
            group: "Intro".into(),
            fade_seconds: 0,
        })
        .await
        .unwrap();
    let before = server.received_requests().await.unwrap();
    assert_eq!(
        before
            .iter()
            .filter(|r| r.url.path() == "/me/player/volume")
            .count(),
        1
    );
    ducking.end("alert", false).await.unwrap();
    assert!(server
        .received_requests()
        .await
        .unwrap()
        .last()
        .unwrap()
        .url
        .query_pairs()
        .any(|(k, v)| k == "volume_percent" && v == "82"));
}

#[tokio::test]
async fn health_recovery_activates_without_playback_and_respects_two_minute_cooldown() {
    let (runtime, _, _, server, _root, _player) = setup().await;
    Mock::given(method("GET"))
        .and(path("/me"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(json!({"id":"user","display_name":"Tester"})),
        )
        .mount(&server)
        .await;
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(204))
        .mount(&server)
        .await;
    _player
        .connect(&ccs_modules::spotify::SpotifyConnectOptions {
            client_id: "client".into(),
            redirect_uri: "http://127.0.0.1:4382/spotify-callback".into(),
            scopes: vec![],
        })
        .await
        .unwrap();
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(ResponseTemplate::new(204))
        .with_priority(1)
        .mount(&server)
        .await;
    let now = chrono::Utc::now();
    runtime.tick_at(now, true).await.unwrap();
    runtime
        .tick_at(now + chrono::Duration::seconds(60), false)
        .await
        .unwrap();
    let transfers = server
        .received_requests()
        .await
        .unwrap()
        .into_iter()
        .filter(|r| r.method.as_str() == "PUT" && r.url.path() == "/me/player")
        .collect::<Vec<_>>();
    assert_eq!(transfers.len(), 1);
    assert_eq!(
        transfers[0].body_json::<Value>().unwrap(),
        json!({"device_ids":["device"],"play":false})
    );
    runtime
        .tick_at(now + chrono::Duration::seconds(121), false)
        .await
        .unwrap();
    assert_eq!(
        server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .filter(|r| r.method.as_str() == "PUT" && r.url.path() == "/me/player")
            .count(),
        2
    );
}
#[tokio::test]
async fn missing_playback_and_invalid_restore_input_do_not_replace_existing_state() {
    let (runtime, _, _, server, _root, _player) = setup().await;
    runtime
        .action(MusicStateAction::Capture {
            group: "Intro".into(),
        })
        .await
        .unwrap();
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(ResponseTemplate::new(204))
        .with_priority(1)
        .mount(&server)
        .await;
    assert!(runtime
        .action(MusicStateAction::Capture {
            group: "Intro".into()
        })
        .await
        .is_err());
    let count = server.received_requests().await.unwrap().len();
    assert!(runtime
        .action(MusicStateAction::Restore {
            group: "Intro".into(),
            fade_seconds: 301
        })
        .await
        .is_err());
    assert_eq!(server.received_requests().await.unwrap().len(), count);
    assert!(runtime.snapshot().await.unwrap()["states"]
        .get("Intro")
        .is_some());
}

#[tokio::test]
async fn artist_context_restore_rejects_unsupported_track_offset_before_changing_playback() {
    let (runtime, _, _, server, root, _) = setup().await;
    runtime
        .action(MusicStateAction::Capture {
            group: "Intro".into(),
        })
        .await
        .unwrap();
    let store = SpotifyStateStore::new(root.path());
    let mut state = store.get("Intro").await.unwrap();
    state.context_uri = "spotify:artist:artist".into();
    store.save("Intro", state).await.unwrap();
    let before = server.received_requests().await.unwrap().len();
    let error = runtime
        .action(MusicStateAction::Restore {
            group: "Intro".into(),
            fade_seconds: 0,
        })
        .await
        .unwrap_err();
    assert!(error.to_string().contains("Künstler"));
    assert_eq!(server.received_requests().await.unwrap().len(), before);
    assert!(store.get("Intro").await.is_ok());
}

async fn connect_player(player: &SpotifyClient, server: &MockServer) {
    Mock::given(method("GET"))
        .and(path("/me"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"id":"user"})))
        .mount(server)
        .await;
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(204))
        .mount(server)
        .await;
    player
        .connect(&ccs_modules::spotify::SpotifyConnectOptions {
            client_id: "client".into(),
            redirect_uri: "http://127.0.0.1:4382/spotify-callback".into(),
            scopes: vec![],
        })
        .await
        .unwrap();
}

#[tokio::test]
async fn health_does_not_interrupt_running_fades_or_run_after_close() {
    let (runtime, engine, _, server, _root, player) = setup().await;
    connect_player(&player, &server).await;
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(ResponseTemplate::new(204))
        .with_priority(1)
        .mount(&server)
        .await;
    let fade = engine.dispatch(MusicAction::FadeTo {
        percent: 20,
        milliseconds: 10000,
        pause_at_end: false,
    });
    for _ in 0..100 {
        if engine.status().await["running"] == true {
            break;
        }
        tokio::time::sleep(Duration::from_millis(5)).await;
    }
    assert_eq!(
        engine.status().await["running"],
        true,
        "{}",
        engine.status().await
    );
    runtime.tick_at(chrono::Utc::now(), false).await.unwrap();
    assert!(!server
        .received_requests()
        .await
        .unwrap()
        .iter()
        .any(|r| r.method.as_str() == "PUT" && r.url.path() == "/me/player"));
    engine.close().await;
    assert!(fade.await.unwrap().is_err());
    let count = server.received_requests().await.unwrap().len();
    runtime
        .tick_at(chrono::Utc::now() + chrono::Duration::minutes(5), false)
        .await
        .unwrap();
    assert_eq!(server.received_requests().await.unwrap().len(), count);
}

#[tokio::test]
async fn slow_health_query_does_not_block_state_snapshot_and_startup_cleanup_persists() {
    let (runtime, _, _, server, root, player) = setup().await;
    runtime
        .action(MusicStateAction::Capture {
            group: "Old".into(),
        })
        .await
        .unwrap();
    let store = SpotifyStateStore::new(root.path());
    let mut old = store.get("Old").await.unwrap();
    old.saved_at_utc = chrono::Utc::now() - chrono::Duration::minutes(181);
    store.save("Old", old).await.unwrap();
    connect_player(&player, &server).await;
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(ResponseTemplate::new(204).set_delay(Duration::from_millis(600)))
        .with_priority(1)
        .mount(&server)
        .await;
    let monitor = runtime.clone();
    let job = tokio::spawn(async move { monitor.tick_at(chrono::Utc::now(), true).await });
    tokio::time::sleep(Duration::from_millis(80)).await;
    let snapshot = tokio::time::timeout(Duration::from_millis(150), runtime.snapshot())
        .await
        .expect("state snapshot blocked by Spotify network")
        .unwrap();
    assert!(snapshot["states"].as_object().unwrap().is_empty());
    job.await.unwrap().unwrap();
    assert!(SpotifyStateStore::new(root.path())
        .get("Old")
        .await
        .is_err());
}

#[tokio::test]
async fn maintenance_cannot_acquire_player_while_a_manual_command_owns_it() {
    let (_, engine, _, server, _root, _) = setup().await;
    let _command = engine.manual_player_guard().await;
    let before = server.received_requests().await.unwrap().len();
    assert!(engine
        .recover_missing_device("client", &json!({"PreferredDeviceId":"device"}))
        .await
        .unwrap()
        .is_none());
    assert_eq!(server.received_requests().await.unwrap().len(), before);
}
