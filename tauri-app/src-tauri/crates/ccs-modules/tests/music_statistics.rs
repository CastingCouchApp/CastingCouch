use ccs_core::{music_statistics::MusicStatisticsStore, store::JsonSettingsStore};
use ccs_modules::{
    music_statistics::MusicStatisticsRuntime,
    spotify::{SpotifyClient, SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet},
};
use ccs_secrets::MemorySecretStore;
use serde_json::json;
use std::{sync::Arc, time::Duration};
use wiremock::{
    matchers::{method, path},
    Mock, MockServer, ResponseTemplate,
};
// These notifications follow real settings/statistics disk writes, including
// sync_all. This is a liveness bound, not a one-second performance requirement.
const IO_TIMEOUT: Duration = Duration::from_secs(5);
async fn wait_for_change(changed: &mut tokio::sync::broadcast::Receiver<()>) {
    tokio::time::timeout(IO_TIMEOUT, changed.recv())
        .await
        .expect("statistics writer did not finish within the disk I/O budget")
        .expect("statistics notification channel closed or lagged");
}
async fn setup() -> (
    Arc<MusicStatisticsRuntime>,
    Arc<SpotifyClient>,
    Arc<JsonSettingsStore>,
    MockServer,
    tempfile::TempDir,
) {
    let root = tempfile::tempdir().unwrap();
    let server = MockServer::start().await;
    let secrets = Arc::new(MemorySecretStore::new());
    SpotifyTokenRepository::new(secrets.clone())
        .save(&SpotifyTokenSet {
            access_token: "token".into(),
            refresh_token: "refresh".into(),
            obtained_at: chrono::Utc::now(),
            expires_in_seconds: 3600,
            token_type: "Bearer".into(),
            scopes: vec![],
        })
        .unwrap();
    Mock::given(method("GET")).and(path("/me/player/currently-playing")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"is_playing":true,"progress_ms":100,"item":{"id":"song","uri":"spotify:track:song","type":"track","name":"Song","artists":[{"name":"Artist"}],"album":{"name":"Album"}}}))).mount(&server).await;
    let player = Arc::new(SpotifyClient::with_http(
        secrets,
        SpotifyOAuthClient::new(),
        server.uri(),
    ));
    let settings = Arc::new(JsonSettingsStore::new(root.path().join("settings.json")));
    let runtime = Arc::new(MusicStatisticsRuntime::new(
        Arc::new(MusicStatisticsStore::new(root.path())),
        settings.clone(),
    ));
    (runtime, player, settings, server, root)
}
#[tokio::test]
async fn fresh_unchanged_http_samples_add_time_without_duplicate_plays_and_survive_restart() {
    let (runtime, player, _, server, root) = setup().await;
    let mut changed = runtime.subscribe();
    let job = tokio::spawn(runtime.bind_spotify(&player));
    let first = player.refresh_now_playing("client").await.unwrap();
    assert_eq!(first.track_id, "song");
    wait_for_change(&mut changed).await;
    tokio::time::sleep(Duration::from_millis(100)).await;
    player.refresh_now_playing("client").await.unwrap();
    wait_for_change(&mut changed).await;
    let data = runtime.snapshot().await.unwrap();
    assert!(data["error"].is_null(), "writer failed: {data}");
    assert_eq!(data["totalPlays"], 1);
    assert!(data["totalListeningSeconds"].as_f64().unwrap() > 0.0);
    assert_eq!(data["topTracks"][0]["Title"], "Song");
    assert_eq!(server.received_requests().await.unwrap().len(), 2);
    assert_eq!(
        MusicStatisticsStore::new(root.path())
            .snapshot()
            .await
            .unwrap()
            .total_plays,
        1
    );
    runtime.close().await;
    tokio::time::timeout(IO_TIMEOUT, job)
        .await
        .unwrap()
        .unwrap();
}
#[tokio::test]
async fn http_failures_and_unselected_provider_break_timeline_and_surface_errors() {
    let (runtime, player, settings, server, _root) = setup().await;
    let mut changed = runtime.subscribe();
    let job = tokio::spawn(runtime.bind_spotify(&player));
    player.refresh_now_playing("client").await.unwrap();
    wait_for_change(&mut changed).await;
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(
            ResponseTemplate::new(500).set_body_json(json!({"error":{"message":"offline"}})),
        )
        .with_priority(2)
        .mount(&server)
        .await;
    assert!(player.refresh_now_playing("client").await.is_err());
    wait_for_change(&mut changed).await;
    let data = runtime.snapshot().await.unwrap();
    assert!(data["error"].as_str().unwrap().contains("offline"));
    assert_eq!(data["totalPlays"], 1);
    let original = settings.read_value().await.unwrap();
    let mut next = original.clone();
    next["MusicPlayer"]["Source"] = json!("ytmusic");
    settings.save_edit(&original, &next).await.unwrap();
    Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({"is_playing":true,"item":{"id":"other","name":"Other","type":"track"}}),
        ))
        .with_priority(1)
        .mount(&server)
        .await;
    player.refresh_now_playing("client").await.unwrap();
    wait_for_change(&mut changed).await;
    assert_eq!(runtime.snapshot().await.unwrap()["totalPlays"], 1);
    settings.save_edit(&next, &original).await.unwrap();
    player.refresh_now_playing("client").await.unwrap();
    wait_for_change(&mut changed).await;
    let data = runtime.snapshot().await.unwrap();
    assert_eq!(data["totalPlays"], 2);
    assert_eq!(data["totalListeningSeconds"], 0.0);
    assert!(data["error"].is_null());
    runtime.close().await;
    tokio::time::timeout(IO_TIMEOUT, job)
        .await
        .unwrap()
        .unwrap();
}
#[tokio::test]
async fn reset_is_persistent_and_closed_runtime_does_not_record_late_samples() {
    let (runtime, player, _, _, root) = setup().await;
    let mut changed = runtime.subscribe();
    let job = tokio::spawn(runtime.bind_spotify(&player));
    player.refresh_now_playing("client").await.unwrap();
    wait_for_change(&mut changed).await;
    runtime.reset().await.unwrap();
    assert_eq!(
        MusicStatisticsStore::new(root.path())
            .snapshot()
            .await
            .unwrap()
            .total_plays,
        0
    );
    runtime.close().await;
    tokio::time::timeout(IO_TIMEOUT, job)
        .await
        .unwrap()
        .unwrap();
    player.refresh_now_playing("client").await.unwrap();
    assert_eq!(runtime.snapshot().await.unwrap()["totalPlays"], 0);
}
