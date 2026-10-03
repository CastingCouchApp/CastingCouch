use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_modules::twitch::{
    TwitchClient, TwitchConnectOptions, TwitchMetricsRuntime, TwitchOAuthClient,
    TwitchTokenRepository, TwitchTokenSet,
};
use ccs_overlay_server::RealtimeHub;
use ccs_secrets::MemorySecretStore;
use serde_json::json;
use std::sync::Arc;
use wiremock::{
    matchers::{method, path, query_param},
    Mock, MockServer, ResponseTemplate,
};

#[test]
fn adaptive_chatter_interval_matches_csharp_threshold_and_normalization() {
    use ccs_modules::twitch::chatter_interval;
    assert_eq!(chatter_interval(&json!({}), 49), 10);
    assert_eq!(chatter_interval(&json!({}), 50), 60);
    assert_eq!(
        chatter_interval(
            &json!({"ChattersRefreshSecondsLow":999,"ChattersRefreshSecondsHigh":1,"ChattersRefreshViewerThreshold":0}),
            0
        ),
        120
    );
    assert_eq!(
        chatter_interval(
            &json!({"ChattersRefreshSecondsLow":999,"ChattersRefreshSecondsHigh":1,"ChattersRefreshViewerThreshold":0}),
            50
        ),
        120
    );
    assert_eq!(
        chatter_interval(&json!({"ChattersRefreshSecondsHigh":9999}), 50),
        600
    );
}

#[tokio::test]
async fn metric_queries_are_independent_report_errors_and_recover_without_fake_zeroes() {
    let server = MockServer::start().await;
    let client_id = "abcdefghijabcdefghijabcdefghij";
    Mock::given(path("/validate")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"client_id":client_id,"login":"owner","user_id":"10","scopes":[],"expires_in":3600}))).mount(&server).await;
    Mock::given(path("/users"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"10","login":"owner","display_name":"Owner"}]}),
            ),
        )
        .mount(&server)
        .await;
    for (endpoint, response) in [
        (
            "/channels",
            json!({"data":[{"title":"Title","game_name":"Game"}]}),
        ),
        ("/streams", json!({"data":[]})),
        ("/channels/followers", json!({"total":30,"data":[]})),
        ("/chat/chatters", json!({"total":10,"data":[]})),
    ] {
        Mock::given(method("GET"))
            .and(path(endpoint))
            .and(query_param(
                if endpoint == "/streams" {
                    "user_id"
                } else {
                    "broadcaster_id"
                },
                "10",
            ))
            .respond_with(ResponseTemplate::new(200).set_body_json(response))
            .mount(&server)
            .await;
    }
    let denied = Mock::given(path("/subscriptions"))
        .respond_with(
            ResponseTemplate::new(403)
                .set_body_json(json!({"message":"Missing subscriptions scope"})),
        )
        .mount_as_scoped(&server)
        .await;
    let secrets = Arc::new(MemorySecretStore::new());
    TwitchTokenRepository::new(secrets.clone())
        .save(&TwitchTokenSet::from_oauth(
            "token".into(),
            "refresh".into(),
            3600,
            vec![],
        ))
        .unwrap();
    let twitch = Arc::new(TwitchClient::with_http(
        secrets,
        TwitchOAuthClient::with_base_urls(
            format!("{}/device", server.uri()),
            format!("{}/token", server.uri()),
            format!("{}/validate", server.uri()),
        ),
        format!("{}/", server.uri()),
    ));
    twitch
        .connect(&TwitchConnectOptions {
            client_id: client_id.into(),
            channel_name: String::new(),
            scopes: vec![],
            enable_event_sub: false,
        })
        .await
        .unwrap();
    let root = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(root.path().into());
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let mut saved = settings.load().await.unwrap();
    saved.twitch.client_id = client_id.into();
    settings.save(&saved).await.unwrap();
    let hub = Arc::new(RealtimeHub::new());
    let runtime = TwitchMetricsRuntime::new(settings.clone(), twitch.clone(), hub.clone());
    assert!(runtime.snapshot().await.subscriptions.value.is_none());
    assert!(runtime.refresh(true).await.unwrap());
    let snapshot = runtime.snapshot().await;
    assert_eq!(snapshot.viewer_count.value, Some(0));
    assert_eq!(snapshot.followers.value, Some(30));
    assert_eq!(snapshot.chatters.value, Some(10));
    assert_eq!(snapshot.subscriptions.value, None);
    assert!(snapshot
        .subscriptions
        .error
        .unwrap()
        .contains("Missing subscriptions scope"));
    assert_eq!(
        hub.live.data.read().unwrap()["twitch"]["followersAvailable"],
        true
    );
    assert_eq!(
        hub.live.data.read().unwrap()["twitch"]["subscriptionsAvailable"],
        false
    );
    assert!(!runtime.refresh(false).await.unwrap());
    assert!(!runtime.notify_event("channel.chat.message"));
    assert!(!runtime.refresh(false).await.unwrap());
    assert!(runtime.notify_event("channel.follow"));
    assert!(runtime.refresh(false).await.unwrap());
    drop(denied);
    let good = Mock::given(path("/subscriptions"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"total":7,"data":[]})))
        .mount_as_scoped(&server)
        .await;
    runtime.refresh(true).await.unwrap();
    assert_eq!(runtime.snapshot().await.subscriptions.value, Some(7));
    assert!(runtime.snapshot().await.subscriptions.error.is_none());
    drop(good);
    Mock::given(path("/subscriptions"))
        .respond_with(ResponseTemplate::new(500).set_body_json(json!({"message":"Later failure"})))
        .mount(&server)
        .await;
    runtime.refresh(true).await.unwrap();
    assert_eq!(runtime.snapshot().await.subscriptions.value, Some(7));
    assert!(runtime
        .snapshot()
        .await
        .subscriptions
        .error
        .unwrap()
        .contains("Later failure"));
    assert_eq!(hub.live.data.read().unwrap()["twitch"]["subscriptions"], 7);
    assert_eq!(
        hub.live.data.read().unwrap()["twitch"]["subscriptionsAvailable"],
        false
    );
    let mut changed = settings.load().await.unwrap();
    changed.twitch.channel_name = "another".into();
    settings.save(&changed).await.unwrap();
    runtime.refresh(true).await.unwrap();
    assert_eq!(runtime.snapshot().await.subscriptions.value, None);
    twitch.logout().await.unwrap();
    runtime.refresh(true).await.unwrap();
    assert!(!runtime.snapshot().await.connected);
    assert_eq!(runtime.snapshot().await.followers.value, Some(30));
    assert_eq!(
        hub.live.data.read().unwrap()["twitch"]["viewerCountAvailable"],
        false
    );
}
