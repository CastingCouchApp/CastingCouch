use super::*;
use crate::spotify::{
    SpotifyConnectOptions, SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet,
};
use ccs_secrets::MemorySecretStore;
use serde_json::json;
use wiremock::{
    matchers::{method, path, query_param},
    Mock, MockServer, ResponseTemplate,
};

async fn setup(
    root: &std::path::Path,
    server: &MockServer,
) -> (
    Arc<MusicPlayerRuntime>,
    Arc<JsonSettingsStore>,
    Arc<SpotifyClient>,
) {
    let store = Arc::new(JsonSettingsStore::new(root.join("settings.json")));
    let mut settings = ccs_core::AppSettings::default();
    settings.spotify.client_id = "contract-client-id-12345".into();
    store.save(&settings).await.unwrap();
    let secrets = Arc::new(MemorySecretStore::new());
    SpotifyTokenRepository::new(secrets.clone())
        .save(&SpotifyTokenSet::from_oauth(
            "access".into(),
            "refresh".into(),
            3600,
            "Bearer".into(),
            vec![],
        ))
        .unwrap();
    let player = Arc::new(SpotifyClient::with_http(
        secrets,
        SpotifyOAuthClient::with_base_urls(
            format!("{}/authorize", server.uri()),
            format!("{}/token", server.uri()),
        ),
        format!("{}/", server.uri()),
    ));
    let ducks = Arc::new(AlertDucking::new(player.clone()));
    let scene = Arc::new(SceneMusicEngine::new(
        store.clone(),
        player.clone(),
        ducks.clone(),
    ));
    let runtime = Arc::new(MusicPlayerRuntime::new(
        store.clone(),
        player.clone(),
        scene,
        ducks,
        Arc::new(Mutex::new(None)),
    ));
    (runtime, store, player)
}

async fn connect(player: &SpotifyClient, server: &MockServer) {
    Mock::given(method("GET"))
        .and(path("/me"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(json!({"id":"user","display_name":"User"})),
        )
        .mount(server)
        .await;
    Mock::given(method("GET")).and(path("/me/player/currently-playing")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"is_playing":true,"progress_ms":1500,"item":{"id":"track","name":"Song","duration_ms":5000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[{"url":"https://example.com/cover"}]}}}))).mount(server).await;
    player
        .connect(&SpotifyConnectOptions {
            client_id: "contract-client-id-12345".into(),
            redirect_uri: Default::default(),
            scopes: vec![],
        })
        .await
        .unwrap();
}

#[tokio::test]
async fn common_snapshot_and_controls_use_spotify_preferences_and_real_playback_volume() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, store, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    let mut settings = store.load().await.unwrap();
    settings.spotify.extra = json!({"PreferredDeviceId":"desktop"});
    store.save(&settings).await.unwrap();
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(
            ResponseTemplate::new(200)
                .set_body_json(json!({"device":{"id":"desktop","volume_percent":42}})),
        )
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/pause"))
        .and(query_param("device_id", "desktop"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    runtime.refresh_details().await;
    let snapshot = runtime.snapshot().await.unwrap();
    assert_eq!(snapshot.provider, "spotify");
    assert!(snapshot.connected && snapshot.supports_seek && snapshot.supports_volume);
    assert_eq!(snapshot.title, "Song");
    assert_eq!(snapshot.cover_url, "https://example.com/cover");
    assert_eq!(snapshot.volume_percent, Some(42));
    runtime.action(MusicPlayerAction::PlayPause).await.unwrap();
    assert_eq!(runtime.snapshot().await.unwrap().volume_percent, Some(42));
    runtime.disconnect().await.unwrap();
    assert!(
        player.has_token(),
        "disconnect must preserve OAuth unlike logout"
    );
    let snapshot = runtime.snapshot().await.unwrap();
    assert!(!snapshot.connected);
    assert!(snapshot.title.is_empty());
    assert_eq!(snapshot.volume_percent, None);
}

#[tokio::test]
async fn provider_switch_stops_inactive_player_and_routes_youtube_commands_without_spotify_requests(
) {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, store, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    let mut settings = store.load().await.unwrap();
    settings.music_player.source = "ytmusic".into();
    store.save(&settings).await.unwrap();
    runtime.apply_provider().await.unwrap();
    assert_eq!(player.status().await.state, ConnectionState::Disconnected);
    assert!(player.has_token());
    let bridge = YouTubeMusicBridge::start(0).await.unwrap();
    let port = bridge.port;
    *runtime.ytm.lock().await = Some(bridge.clone());
    let http = reqwest::Client::new();
    http.post(format!("http://127.0.0.1:{port}/ytmusic/state"))
        .json(&json!({"title":"YT Song","isPlaying":true}))
        .send()
        .await
        .unwrap()
        .error_for_status()
        .unwrap();
    let snapshot = runtime.snapshot().await.unwrap();
    assert_eq!(snapshot.title, "YT Song");
    assert!(!snapshot.supports_seek && !snapshot.supports_volume);
    assert_eq!(snapshot.volume_percent, None);
    runtime.action(MusicPlayerAction::Next).await.unwrap();
    assert!(runtime
        .action_for_provider("spotify", MusicPlayerAction::Next)
        .await
        .is_err());
    assert!(runtime
        .action(MusicPlayerAction::Seek { position_ms: 20 })
        .await
        .unwrap_err()
        .to_string()
        .contains("YouTube Music"));
    assert!(runtime
        .action(MusicPlayerAction::Volume { percent: 25 })
        .await
        .is_err());
    let commands: Value = http
        .get(format!("http://127.0.0.1:{port}/ytmusic/commands"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(commands["commands"], json!(["next"]));
    settings.music_player.source = "spotify".into();
    store.save(&settings).await.unwrap();
    runtime.apply_provider().await.unwrap();
    assert!(!bridge.is_running());
    assert!(runtime.ytm.lock().await.is_none());
    std::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, port)).unwrap();
}

#[tokio::test]
async fn disconnected_actions_fail_before_http_and_failed_refresh_surfaces_error_without_inventing_volume(
) {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, _, player) = setup(root.path(), &server).await;
    assert!(runtime.action(MusicPlayerAction::Play).await.is_err());
    assert!(server.received_requests().await.unwrap().is_empty());
    connect(&player, &server).await;
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(ResponseTemplate::new(503))
        .mount(&server)
        .await;
    runtime.refresh_details().await;
    let snapshot = runtime.snapshot().await.unwrap();
    assert_eq!(snapshot.volume_percent, None);
    assert!(snapshot.error.unwrap().contains("503"));
}

#[tokio::test]
async fn delayed_playback_response_cannot_restore_volume_after_provider_switch() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, store, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(
            ResponseTemplate::new(200)
                .set_delay(std::time::Duration::from_millis(150))
                .set_body_json(json!({"device":{"volume_percent":80}})),
        )
        .mount(&server)
        .await;
    let refreshing = {
        let runtime = runtime.clone();
        tokio::spawn(async move {
            runtime.refresh_details().await;
        })
    };
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        loop {
            if server
                .received_requests()
                .await
                .unwrap()
                .iter()
                .any(|r| r.url.path() == "/me/player")
            {
                break;
            }
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    let mut settings = store.load().await.unwrap();
    settings.music_player.source = "ytmusic".into();
    store.save(&settings).await.unwrap();
    runtime.apply_provider().await.unwrap();
    refreshing.await.unwrap();
    settings.music_player.source = "spotify".into();
    store.save(&settings).await.unwrap();
    runtime.apply_provider().await.unwrap();
    connect(&player, &server).await;
    assert_eq!(runtime.snapshot().await.unwrap().volume_percent, None);
}

#[tokio::test]
async fn manual_volume_wins_over_an_older_playback_refresh() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, _, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    runtime.apply_provider().await.unwrap();
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(
            ResponseTemplate::new(200)
                .set_delay(std::time::Duration::from_millis(150))
                .set_body_json(json!({"device":{"volume_percent":80}})),
        )
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "25"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let refreshing = {
        let runtime = runtime.clone();
        tokio::spawn(async move {
            runtime.refresh_details().await;
        })
    };
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        loop {
            if server
                .received_requests()
                .await
                .unwrap()
                .iter()
                .any(|r| r.url.path() == "/me/player")
            {
                break;
            }
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    runtime
        .action(MusicPlayerAction::Volume { percent: 25 })
        .await
        .unwrap();
    refreshing.await.unwrap();
    assert_eq!(runtime.snapshot().await.unwrap().volume_percent, Some(25));
}

#[tokio::test]
async fn pending_login_is_visible_and_common_disconnect_cancels_the_callback_listener() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, _, player) = setup(root.path(), &server).await;
    let socket = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = socket.local_addr().unwrap().port();
    drop(socket);
    player
        .begin_login(SpotifyConnectOptions {
            client_id: "contract-client-id-12345".into(),
            redirect_uri: format!("http://127.0.0.1:{port}/callback/"),
            scopes: vec![],
        })
        .await
        .unwrap();
    let snapshot = runtime.snapshot().await.unwrap();
    assert!(snapshot.connecting);
    assert!(!snapshot.connected);
    assert_eq!(snapshot.status_text, "Verbinde …");
    runtime.disconnect().await.unwrap();
    assert!(!runtime.snapshot().await.unwrap().connecting);
    std::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, port)).unwrap();
}

#[test]
fn requested_volume_precedes_delayed_reporting_for_exactly_four_seconds() {
    let now = std::time::Instant::now();
    let details = PlaybackDetails {
        volume: Some(80),
        requested: Some((0, now)),
        ..Default::default()
    };
    assert_eq!(
        details.effective_volume(now + std::time::Duration::from_secs(3)),
        Some(0)
    );
    assert_eq!(
        details.effective_volume(now + std::time::Duration::from_secs(4)),
        Some(80)
    );
}

#[tokio::test]
async fn transient_empty_playback_and_api_errors_preserve_verified_track_until_disconnect() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, _, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    assert_eq!(runtime.snapshot().await.unwrap().title, "Song");
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(204))
        .with_priority(2)
        .mount(&server)
        .await;
    player
        .refresh_now_playing("contract-client-id-12345")
        .await
        .unwrap();
    assert_eq!(runtime.snapshot().await.unwrap().title, "Song");
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(503))
        .with_priority(1)
        .mount(&server)
        .await;
    assert!(player
        .refresh_now_playing("contract-client-id-12345")
        .await
        .is_err());
    let snapshot = runtime.snapshot().await.unwrap();
    assert!(snapshot.connected);
    assert_eq!(snapshot.title, "Song");
    assert!(snapshot.error.unwrap().contains("503"));
    runtime.disconnect().await.unwrap();
    let snapshot = runtime.snapshot().await.unwrap();
    assert!(!snapshot.connected);
    assert!(snapshot.title.is_empty());
}

#[tokio::test]
async fn successful_commands_update_playback_without_fabricating_statistics_samples() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, _, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    runtime.apply_provider().await.unwrap();
    runtime.snapshot().await.unwrap();
    let mut events = player.subscribe_now_playing();
    let mut samples = player.subscribe_playback_samples();
    for endpoint in ["pause", "play", "seek"] {
        Mock::given(method("PUT"))
            .and(path(format!("/me/player/{endpoint}")))
            .respond_with(ResponseTemplate::new(204))
            .mount(&server)
            .await;
    }
    runtime.action(MusicPlayerAction::Pause).await.unwrap();
    assert!(!runtime.snapshot().await.unwrap().is_playing);
    assert!(!player.now_playing().await.is_playing);
    assert!(!events.try_recv().unwrap().is_playing);
    runtime.action(MusicPlayerAction::Play).await.unwrap();
    assert!(runtime.snapshot().await.unwrap().is_playing);
    assert!(events.try_recv().unwrap().is_playing);
    runtime
        .action(MusicPlayerAction::Seek { position_ms: 9000 })
        .await
        .unwrap();
    assert_eq!(runtime.snapshot().await.unwrap().progress_ms, 5000);
    assert_eq!(events.try_recv().unwrap().progress_ms, 5000);
    assert!(matches!(
        samples.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    assert!(server
        .received_requests()
        .await
        .unwrap()
        .iter()
        .any(|r| r.url.path() == "/me/player/seek"
            && r.url
                .query_pairs()
                .any(|(k, v)| k == "position_ms" && v == "5000")));
    Mock::given(method("PUT"))
        .and(path("/me/player/pause"))
        .respond_with(ResponseTemplate::new(403))
        .with_priority(1)
        .up_to_n_times(1)
        .mount(&server)
        .await;
    assert!(runtime.action(MusicPlayerAction::Pause).await.is_err());
    assert!(runtime.snapshot().await.unwrap().is_playing);
    assert_eq!(runtime.snapshot().await.unwrap().progress_ms, 5000);
    assert!(events.try_recv().is_err());
    // Even an empty API response must not make a successful manual pause invisible.
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(204))
        .with_priority(1)
        .mount(&server)
        .await;
    player
        .refresh_now_playing("contract-client-id-12345")
        .await
        .unwrap();
    runtime.action(MusicPlayerAction::Pause).await.unwrap();
    let snapshot = runtime.snapshot().await.unwrap();
    assert_eq!(snapshot.title, "Song");
    assert!(!snapshot.is_playing);
}

#[tokio::test]
async fn play_pause_uses_fresh_external_state_and_read_failure_sends_no_command() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, _, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    assert!(runtime.snapshot().await.unwrap().is_playing);
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "is_playing":false,"progress_ms":2000,
            "item":{"id":"track","name":"Song","duration_ms":5000}})))
        .with_priority(2)
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/play"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    runtime.action(MusicPlayerAction::PlayPause).await.unwrap();
    assert!(runtime.snapshot().await.unwrap().is_playing);
    assert_eq!(runtime.snapshot().await.unwrap().progress_ms, 2000);
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(503))
        .with_priority(1)
        .mount(&server)
        .await;
    assert!(runtime
        .action(MusicPlayerAction::PlayPause)
        .await
        .unwrap_err()
        .to_string()
        .contains("503"));
    assert!(runtime.snapshot().await.unwrap().is_playing);
    assert_eq!(
        server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .filter(|r| r.method == "PUT")
            .count(),
        1
    );
}

#[tokio::test]
async fn older_poll_cannot_undo_a_successful_pause_or_emit_an_obsolete_measurement() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, _, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    runtime.apply_provider().await.unwrap();
    Mock::given(method("GET")).and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(200).set_delay(std::time::Duration::from_millis(200))
            .set_body_json(json!({"is_playing":true,"item":{"id":"track","name":"Song","duration_ms":5000}})))
        .with_priority(1).mount(&server).await;
    Mock::given(method("PUT"))
        .and(path("/me/player/pause"))
        .respond_with(ResponseTemplate::new(204))
        .mount(&server)
        .await;
    let mut samples = player.subscribe_playback_samples();
    let polling = {
        let player = player.clone();
        tokio::spawn(async move {
            player
                .refresh_now_playing("contract-client-id-12345")
                .await
                .unwrap()
        })
    };
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        loop {
            if server
                .received_requests()
                .await
                .unwrap()
                .iter()
                .filter(|r| r.url.path() == "/me/player/currently-playing")
                .count()
                >= 2
            {
                break;
            }
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    runtime.action(MusicPlayerAction::Pause).await.unwrap();
    assert!(!polling.await.unwrap().is_playing);
    assert!(!runtime.snapshot().await.unwrap().is_playing);
    assert!(matches!(
        samples.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
}

#[tokio::test]
async fn connected_session_without_a_track_survives_transient_errors_until_disconnect() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, _, player) = setup(root.path(), &server).await;
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(204))
        .with_priority(2)
        .mount(&server)
        .await;
    connect(&player, &server).await;
    let snapshot = runtime.snapshot().await.unwrap();
    assert!(snapshot.connected);
    assert!(snapshot.title.is_empty());
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(503))
        .with_priority(1)
        .mount(&server)
        .await;
    assert!(player
        .refresh_now_playing("contract-client-id-12345")
        .await
        .is_err());
    let snapshot = runtime.snapshot().await.unwrap();
    assert!(snapshot.connected);
    assert!(snapshot.error.unwrap().contains("503"));
    runtime.disconnect().await.unwrap();
    assert!(!runtime.snapshot().await.unwrap().connected);
}

#[tokio::test]
async fn slow_toggle_read_keeps_snapshot_responsive_and_releases_provider_guard_after_timeout() {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let (runtime, store, player) = setup(root.path(), &server).await;
    connect(&player, &server).await;
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(204).set_delay(std::time::Duration::from_secs(4)))
        .with_priority(1)
        .mount(&server)
        .await;
    let command = {
        let runtime = runtime.clone();
        tokio::spawn(async move { runtime.action(MusicPlayerAction::PlayPause).await })
    };
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        loop {
            if server
                .received_requests()
                .await
                .unwrap()
                .iter()
                .filter(|r| r.url.path() == "/me/player/currently-playing")
                .count()
                >= 2
            {
                break;
            }
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    let snapshot = tokio::time::timeout(std::time::Duration::from_millis(200), runtime.snapshot())
        .await
        .unwrap()
        .unwrap();
    assert!(snapshot.connected && snapshot.is_playing);
    assert!(
        tokio::time::timeout(std::time::Duration::from_secs(4), command)
            .await
            .unwrap()
            .unwrap()
            .unwrap_err()
            .to_string()
            .contains("Zeitüberschreitung")
    );
    assert!(!server
        .received_requests()
        .await
        .unwrap()
        .iter()
        .any(|r| r.method == "PUT"));
    let mut settings = store.load().await.unwrap();
    settings.music_player.source = "ytmusic".into();
    store.save(&settings).await.unwrap();
    tokio::time::timeout(
        std::time::Duration::from_millis(200),
        runtime.apply_provider(),
    )
    .await
    .unwrap()
    .unwrap();
    assert_eq!(runtime.snapshot().await.unwrap().provider, "ytmusic");
}
