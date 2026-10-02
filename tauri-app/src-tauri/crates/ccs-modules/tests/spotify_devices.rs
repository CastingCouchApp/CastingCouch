use ccs_modules::spotify::{
    SpotifyAction, SpotifyClient, SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet,
};
use ccs_secrets::MemorySecretStore;
use serde_json::json;
use std::sync::Arc;
use wiremock::{
    matchers::{body_json, method, path, query_param},
    Mock, MockServer, ResponseTemplate,
};
async fn player(devices: serde_json::Value) -> (SpotifyClient, MockServer) {
    let server = MockServer::start().await;
    let secrets = Arc::new(MemorySecretStore::new());
    SpotifyTokenRepository::new(secrets.clone())
        .save(&SpotifyTokenSet {
            access_token: "test-token".into(),
            refresh_token: "refresh".into(),
            obtained_at: chrono::Utc::now(),
            expires_in_seconds: 3600,
            token_type: "Bearer".into(),
            scopes: vec![],
        })
        .unwrap();
    Mock::given(method("GET"))
        .and(path("/me/player/devices"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"devices":devices})))
        .mount(&server)
        .await;
    Mock::given(method("GET"))
        .and(path("/me/player"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"device":{"id":"current"}})))
        .mount(&server)
        .await;
    (
        SpotifyClient::with_http(secrets, SpotifyOAuthClient::new(), server.uri()),
        server,
    )
}
#[tokio::test]
async fn activates_preferred_device_with_explicit_play_state() {
    let (client, server) =
        player(json!([{"id":"preferred","name":"Studio","is_active":false,"is_restricted":false}]))
            .await;
    Mock::given(method("PUT"))
        .and(path("/me/player"))
        .and(body_json(json!({"device_ids":["preferred"],"play":false})))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let result = client
        .activate_preferred_device("client", &json!({"PreferredDeviceId":"preferred"}), false)
        .await
        .unwrap();
    assert_eq!(result["name"], "Studio");
    server.verify().await;
}
#[tokio::test]
async fn unavailable_preference_can_use_active_device_but_restricted_devices_fail() {
    let (client,server)=player(json!([{"id":"locked","name":"Locked","is_active":false,"is_restricted":true},{"id":"current","name":"Active","is_active":true,"is_restricted":false}])).await;
    let device = client
        .activate_preferred_device(
            "client",
            &json!({"PreferredDeviceId":"missing","UseActiveDeviceWhenPreferredUnavailable":true}),
            false,
        )
        .await
        .unwrap();
    assert_eq!(device["id"], "current");
    assert!(!server
        .received_requests()
        .await
        .unwrap()
        .iter()
        .any(|r| r.method.as_str() == "PUT"));
    assert!(client
        .activate_preferred_device(
            "client",
            &json!({"PreferredDeviceId":"missing","UseActiveDeviceWhenPreferredUnavailable":false}),
            false
        )
        .await
        .unwrap_err()
        .to_string()
        .contains("nicht erreichbar"));
    assert!(client
        .activate_preferred_device("client", &json!({"PreferredDeviceId":"locked"}), false)
        .await
        .unwrap_err()
        .to_string()
        .contains("eingeschränkt"));
}
#[tokio::test]
async fn player_commands_target_saved_device_without_changing_library_requests() {
    let (client, server) = player(json!([])).await;
    Mock::given(method("POST"))
        .and(path("/me/player/next"))
        .and(query_param("device_id", "preferred"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    client
        .action_on_device("client", SpotifyAction::Next, Some("preferred"))
        .await
        .unwrap();
    Mock::given(method("PUT"))
        .and(path("/me/library"))
        .and(query_param("uris", "spotify:track:track123"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    client
        .action_on_device(
            "client",
            SpotifyAction::SaveTrack {
                id: "track123".into(),
            },
            Some("preferred"),
        )
        .await
        .unwrap();
    let requests = server.received_requests().await.unwrap();
    assert!(!requests
        .iter()
        .find(|r| r.url.path() == "/me/library")
        .unwrap()
        .url
        .query_pairs()
        .any(|(k, _)| k == "device_id"));
}

#[tokio::test]
async fn playlist_auto_transfer_uses_fallback_and_can_be_disabled() {
    let (client, server) =
        player(json!([{"id":"current","name":"Active","is_active":true,"is_restricted":false}]))
            .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/shuffle"))
        .respond_with(ResponseTemplate::new(204))
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/me/player/play"))
        .and(query_param("device_id", "current"))
        .and(body_json(json!({"context_uri":"spotify:playlist:list"})))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    client
        .action_with_preferences(
            "client",
            SpotifyAction::PlayPlaylist {
                uri: "spotify:playlist:list".into(),
            },
            &json!({"PreferredDeviceId":"missing","AutoTransferToPreferredDevice":true}),
        )
        .await
        .unwrap();
    server.verify().await;
    let before = server.received_requests().await.unwrap().len();
    Mock::given(method("PUT"))
        .and(path("/me/player/play"))
        .and(query_param("device_id", "missing"))
        .respond_with(
            ResponseTemplate::new(404)
                .set_body_json(json!({"error":{"message":"Device unavailable"}})),
        )
        .expect(1)
        .mount(&server)
        .await;
    assert!(client
        .action_with_preferences(
            "client",
            SpotifyAction::PlayPlaylist {
                uri: "spotify:playlist:list".into()
            },
            &json!({"PreferredDeviceId":"missing","AutoTransferToPreferredDevice":false})
        )
        .await
        .is_err());
    assert_eq!(server.received_requests().await.unwrap().len(), before + 1);
}

#[tokio::test]
async fn idle_ducking_coordinator_respects_preferred_device_for_manual_volume() {
    let (client, server) = player(json!([])).await;
    Mock::given(method("PUT"))
        .and(path("/me/player/volume"))
        .and(query_param("device_id", "preferred"))
        .and(query_param("volume_percent", "55"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let ducking = ccs_modules::music_automation::AlertDucking::new(Arc::new(client));
    ducking
        .set_volume_on_device("client", 55, Some("preferred"))
        .await
        .unwrap();
}
