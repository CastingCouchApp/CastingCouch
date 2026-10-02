use ccs_modules::{
    music_automation::AlertDucking,
    spotify::{SpotifyClient, SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet},
};
use ccs_secrets::MemorySecretStore;
use serde_json::json;
use std::sync::Arc;
use wiremock::{
    matchers::{method, path, query_param},
    Mock, MockServer, ResponseTemplate,
};

async fn player() -> (Arc<SpotifyClient>, MockServer) {
    let server = MockServer::start().await;
    let secrets = Arc::new(MemorySecretStore::new());
    SpotifyTokenRepository::new(secrets.clone())
        .save(&SpotifyTokenSet {
            access_token: "test-token".into(),
            refresh_token: "refresh".into(),
            obtained_at: chrono::Utc::now(),
            expires_in_seconds: 3600,
            token_type: "Bearer".into(),
            scopes: vec!["user-modify-playback-state".into()],
        })
        .unwrap();
    let client = Arc::new(SpotifyClient::with_http(
        secrets,
        SpotifyOAuthClient::new(),
        server.uri(),
    ));
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({"is_playing":true,"device":{"id":"original","volume_percent":40}}),
        ))
        .mount(&server)
        .await;
    (client, server)
}

#[tokio::test]
async fn overlapping_alerts_and_queue_restore_only_once_on_the_original_device() {
    let (player, server) = player().await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("device_id", "original"))
        .and(query_param("volume_percent", "20"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("device_id", "original"))
        .and(query_param("volume_percent", "40"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let ducking = AlertDucking::new(player);
    let options =
        json!({"MuteDuringAlerts":true,"FadeDuringAlerts":false,"AlertMuteVolumePercent":20});
    ducking.begin("first", "client", &options).await.unwrap();
    ducking.begin("second", "client", &options).await.unwrap();
    ducking.begin("second", "client", &options).await.unwrap(); // duplicate activity
    ducking.end("first", false).await.unwrap();
    ducking.end("second", true).await.unwrap(); // the next queued alert keeps it lowered
    assert_eq!(
        server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .filter(|r| r.url.path() == "/me/player/volume")
            .count(),
        1
    );
    ducking.begin("third", "client", &options).await.unwrap();
    ducking.end("third", false).await.unwrap();
    ducking.end("third", false).await.unwrap(); // duplicate end never restores twice
    server.verify().await;
}

#[tokio::test]
async fn ducking_never_increases_an_already_quiet_player() {
    let (player, server) = player().await;
    let ducking = AlertDucking::new(player);
    ducking
        .begin(
            "alert",
            "client",
            &json!({"FadeDuringAlerts":false,"AlertMuteVolumePercent":75}),
        )
        .await
        .unwrap();
    ducking.end("alert", false).await.unwrap();
    assert!(server
        .received_requests()
        .await
        .unwrap()
        .iter()
        .all(|r| r.url.path() != "/me/player/volume"));
}

#[tokio::test]
async fn failed_restore_retains_original_volume_for_a_retry() {
    let (player, server) = player().await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "10"))
        .respond_with(ResponseTemplate::new(204))
        .mount(&server)
        .await;
    let failing = Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "40"))
        .respond_with(ResponseTemplate::new(503))
        .mount_as_scoped(&server)
        .await;
    let ducking = AlertDucking::new(player);
    ducking
        .begin(
            "alert",
            "client",
            &json!({"FadeDuringAlerts":false,"AlertMuteVolumePercent":10}),
        )
        .await
        .unwrap();
    assert!(ducking.end("alert", false).await.is_err());
    drop(failing);
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "40"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    ducking.end("alert", false).await.unwrap();
    server.verify().await;
}

#[tokio::test]
async fn manual_volume_changes_during_an_alert_set_the_restore_volume() {
    let (player, server) = player().await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "10"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("device_id", "original"))
        .and(query_param("volume_percent", "60"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let ducking = AlertDucking::new(player);
    ducking
        .begin(
            "alert",
            "client",
            &json!({"FadeDuringAlerts":false,"AlertMuteVolumePercent":10}),
        )
        .await
        .unwrap();
    assert_eq!(
        ducking.set_volume("client", 60).await.unwrap()["deferred"],
        true
    );
    ducking.end("alert", false).await.unwrap();
    server.verify().await;
}

#[tokio::test]
async fn cancellation_after_a_volume_request_still_restores_the_device() {
    let (player, server) = player().await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "10"))
        .respond_with(ResponseTemplate::new(204).set_delay(std::time::Duration::from_secs(1)))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "40"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let ducking = Arc::new(AlertDucking::new(player));
    let background = ducking.clone();
    let task = tokio::spawn(async move {
        background
            .begin(
                "alert",
                "client",
                &json!({"FadeDuringAlerts":false,"AlertMuteVolumePercent":10}),
            )
            .await
    });
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        while !server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .any(|r| r.url.path() == "/me/player/volume")
        {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    task.abort();
    let _ = task.await;
    ducking.end("alert", false).await.unwrap();
    server.verify().await;
}

#[tokio::test]
async fn actual_alert_worker_restores_music_on_disable_and_shutdown() {
    use ccs_modules::{
        alerts::{AlertEngine, MemorySettingsStore},
        overlay_bridge::OverlayEventBridge,
    };
    let (player, server) = player().await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "10"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("volume_percent", "40"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let mut settings = ccs_core::AppSettings::default();
    settings.spotify.client_id = "client".into();
    settings.spotify.extra = json!({"FadeDuringAlerts":false,"AlertMuteVolumePercent":10});
    settings.alerts.inter_alert_delay_milliseconds = 0;
    settings
        .alerts
        .definitions
        .get_mut("Follow")
        .unwrap()
        .duration_seconds = 60;
    let engine = AlertEngine::from_memory(
        Arc::new(MemorySettingsStore::new(settings)),
        OverlayEventBridge::new(Arc::new(ccs_overlay_server::RealtimeHub::new())),
    );
    engine.attach_music(Arc::new(AlertDucking::new(player)));
    engine.test_alert("Follow", "Alice").await.unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        while !server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .any(|r| r.url.path() == "/me/player/volume")
        {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    engine.test_alert("Follow", "Bob").await.unwrap();
    engine.set_runtime(Some(false), None).await.unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(2), engine.shutdown())
        .await
        .unwrap()
        .unwrap();
    assert_eq!(engine.pending_count(), 0);
    assert!(engine.runtime().await.unwrap().last_error.is_none());
    server.verify().await;
}
