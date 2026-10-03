use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_modules::{
    overlay_bridge::OverlayEventBridge,
    twitch::{
        ModerationAction, ModerationRuntime, TwitchClient, TwitchConnectOptions, TwitchOAuthClient,
        TwitchTokenRepository, TwitchTokenSet,
    },
};
use ccs_overlay_server::RealtimeHub;
use ccs_secrets::MemorySecretStore;
use serde_json::{json, Value};
use std::{path::Path, sync::Arc};
use wiremock::{
    matchers::{body_json, header, method, path, query_param},
    Mock, MockServer, ResponseTemplate,
};

async fn setup(
    root: &Path,
) -> (
    MockServer,
    Arc<TwitchClient>,
    ModerationRuntime,
    Arc<RealtimeHub>,
) {
    let server = MockServer::start().await;
    Mock::given(path("/validate")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"client_id":"abcdefghijabcdefghijabcdefghij","login":"owner","user_id":"10","scopes":[],"expires_in":3600}))).mount(&server).await;
    Mock::given(method("GET"))
        .and(path("/users"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"10","login":"owner","display_name":"Owner"}]}),
            ),
        )
        .mount(&server)
        .await;
    Mock::given(method("GET"))
        .and(path("/users"))
        .and(query_param("login", "Alice"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"42","login":"alice","display_name":"Alice"}]}),
            ),
        )
        .with_priority(1)
        .mount(&server)
        .await;
    Mock::given(method("GET"))
        .and(path("/users"))
        .and(query_param("login", "missing"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[]})))
        .with_priority(1)
        .mount(&server)
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
            client_id: "abcdefghijabcdefghijabcdefghij".into(),
            channel_name: String::new(),
            scopes: vec![],
            enable_event_sub: false,
        })
        .await
        .unwrap();
    let paths = AppPaths::from_root(root.into());
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let mut saved = settings.load().await.unwrap();
    saved.twitch.client_id = "abcdefghijabcdefghijabcdefghij".into();
    settings.save(&saved).await.unwrap();
    let hub = Arc::new(RealtimeHub::new());
    let runtime = ModerationRuntime::new(
        settings,
        twitch.clone(),
        OverlayEventBridge::new(hub.clone()),
        &paths.logs,
    );
    (server, twitch, runtime, hub)
}
fn action(value: Value) -> ModerationAction {
    serde_json::from_value(value).unwrap()
}

#[tokio::test]
async fn moderation_view_is_bounded_while_full_log_and_failed_exports_remain_intact() {
    let root = tempfile::tempdir().unwrap();
    let (server, _, runtime, _) = setup(root.path()).await;
    Mock::given(method("DELETE"))
        .and(path("/moderation/bans"))
        .respond_with(ResponseTemplate::new(204))
        .expect(105)
        .mount(&server)
        .await;
    for index in 0..105 {
        runtime
            .execute(action(
                json!({"action":"unban","user":format!("user{index}"),"byId":true}),
            ))
            .await
            .unwrap();
    }
    let entries = runtime.snapshot().await.entries;
    assert_eq!(entries.len(), 100);
    assert!(entries[0].contains("@user104"));
    assert!(entries[99].contains("@user5"));
    let file = root.path().join("Logs/twitch-moderation.log");
    let original = std::fs::read(&file).unwrap();
    assert_eq!(String::from_utf8_lossy(&original).lines().count(), 105);
    let alias = root.path().join("alias.txt");
    std::fs::hard_link(&file, &alias).unwrap();
    runtime.export(&alias).await.unwrap();
    assert_eq!(std::fs::read(&file).unwrap(), original);
    let blocked = root.path().join("blocked");
    std::fs::create_dir(&blocked).unwrap();
    std::fs::write(blocked.join("keep.txt"), b"keep").unwrap();
    assert!(runtime.export(&blocked).await.is_err());
    assert_eq!(std::fs::read(blocked.join("keep.txt")).unwrap(), b"keep");
    assert_eq!(std::fs::read(&file).unwrap(), original);
    assert!(!std::fs::read_dir(root.path()).unwrap().any(|entry| entry
        .unwrap()
        .file_name()
        .to_string_lossy()
        .ends_with(".tmp")));
}

#[tokio::test]
async fn moderation_resolves_logins_clamps_timeouts_and_updates_both_chat_buffers_after_http_success(
) {
    let root = tempfile::tempdir().unwrap();
    let (server, _, runtime, hub) = setup(root.path()).await;
    Mock::given(method("POST"))
        .and(path("/moderation/bans"))
        .and(header("Authorization", "Bearer token"))
        .and(header("Client-Id", "abcdefghijabcdefghijabcdefghij"))
        .and(query_param("broadcaster_id", "10"))
        .and(query_param("moderator_id", "10"))
        .and(body_json(
            json!({"data":{"user_id":"42","reason":"Spam","duration":1209600}}),
        ))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{"user_id":"42"}]})))
        .expect(1)
        .mount(&server)
        .await;
    runtime.bridge().from_twitch(
        "channel.chat.message",
        "Nachricht",
        "2026-10-03T12:00:00Z".parse().unwrap(),
        std::collections::BTreeMap::from([
            ("messageId".into(), "m".into()),
            ("userId".into(), "42".into()),
        ]),
    );
    let mut events = hub.subscribe();
    let result = runtime.execute(action(json!({"action":"timeout","user":" @@Alice ","byId":false,"minutes":999999,"reason":" Spam "}))).await.unwrap();
    assert!(result.applied);
    assert!(result.warnings.is_empty());
    assert!(result.message.contains("20160 Minuten"));
    let event: Value = serde_json::from_str(&events.recv().await.unwrap()).unwrap();
    assert_eq!(event["source"], "app");
    assert_eq!(event["type"], "channel.chat.clear_user_messages");
    assert_eq!(event["data"]["target_user_id"], "42");
    assert!(hub.history()["events"].as_array().unwrap().is_empty());
    assert!(runtime.bridge().twitch_chat_feed()["events"]
        .as_array()
        .unwrap()
        .is_empty());
    assert!(runtime.snapshot().await.entries[0].contains("TIMEOUT · @Alice · Grund: Spam"));
    Mock::given(method("DELETE"))
        .and(path("/moderation/bans"))
        .and(query_param("user_id", "42"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    runtime
        .execute(action(
            json!({"action":"unban","user":"Alice","byId":false}),
        ))
        .await
        .unwrap();
    assert!(runtime.snapshot().await.entries[0].contains("AUFHEBEN · @Alice"));
    let file = root.path().join("Logs/twitch-moderation.log");
    let original = std::fs::read(&file).unwrap();
    assert!(original.starts_with(&[0xef, 0xbb, 0xbf]));
    runtime.clear_view().await;
    assert!(runtime.snapshot().await.entries.is_empty());
    assert_eq!(std::fs::read(&file).unwrap(), original);
    let export = root.path().join("export.txt");
    runtime.export(&export).await.unwrap();
    assert_eq!(std::fs::read(&export).unwrap(), original);
    assert!(runtime.export(&file).await.is_err());
}

#[tokio::test]
async fn invalid_targets_and_denied_moderation_never_clear_chat_and_remain_visible_in_the_log() {
    let root = tempfile::tempdir().unwrap();
    let (server, _, runtime, hub) = setup(root.path()).await;
    runtime.bridge().from_twitch(
        "channel.chat.message",
        "Erhalten",
        "2026-10-03T12:00:00Z".parse().unwrap(),
        std::collections::BTreeMap::from([
            ("messageId".into(), "m".into()),
            ("userId".into(), "42".into()),
        ]),
    );
    for value in [
        json!({"action":"timeout","user":"Alice","byId":false,"minutes":0,"reason":""}),
        json!({"action":"ban","user":" ","byId":false,"reason":""}),
        json!({"action":"delete_message","messageId":" "}),
    ] {
        assert!(runtime.execute(action(value)).await.is_err());
    }
    assert!(runtime.snapshot().await.entries.is_empty());
    assert!(runtime
        .execute(action(
            json!({"action":"ban","user":"missing","byId":false,"reason":""})
        ))
        .await
        .unwrap_err()
        .contains("nicht gefunden"));
    assert!(runtime
        .execute(action(
            json!({"action":"ban","user":"owner","byId":false,"reason":""})
        ))
        .await
        .unwrap_err()
        .contains("eigene Kanal"));
    Mock::given(method("POST"))
        .and(path("/moderation/bans"))
        .respond_with(
            ResponseTemplate::new(403).set_body_json(json!({"message":"Missing moderation scope"})),
        )
        .mount(&server)
        .await;
    assert!(runtime
        .execute(action(
            json!({"action":"ban","user":"42","byId":true,"reason":"Spam"})
        ))
        .await
        .unwrap_err()
        .contains("Missing moderation scope"));
    assert_eq!(hub.history()["events"].as_array().unwrap().len(), 1);
    assert_eq!(
        runtime.bridge().twitch_chat_feed()["events"]
            .as_array()
            .unwrap()
            .len(),
        1
    );
    assert!(runtime.snapshot().await.entries[0].contains("BAN FEHLER"));
    assert_eq!(
        server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .filter(|r| r.url.path() == "/moderation/bans")
            .count(),
        1
    );
}

#[tokio::test]
async fn delete_and_clear_preserve_csharp_log_bytes_and_report_success_separately_from_write_failure(
) {
    let root = tempfile::tempdir().unwrap();
    let (server, _, runtime, hub) = setup(root.path()).await;
    let logs = root.path().join("Logs");
    std::fs::create_dir_all(&logs).unwrap();
    std::fs::write(
        logs.join("twitch-moderation.log"),
        b"\xef\xbb\xbfLegacy\r\n",
    )
    .unwrap();
    for (id, user) in [("m1", "42"), ("m2", "43")] {
        runtime.bridge().from_twitch(
            "channel.chat.message",
            "Nachricht",
            "2026-10-03T12:00:00Z".parse().unwrap(),
            std::collections::BTreeMap::from([
                ("messageId".into(), id.into()),
                ("userId".into(), user.into()),
            ]),
        );
    }
    Mock::given(method("DELETE"))
        .and(path("/moderation/chat"))
        .respond_with(ResponseTemplate::new(204))
        .expect(2)
        .mount(&server)
        .await;
    runtime
        .execute(action(
            json!({"action":"delete_message","messageId":" m1 "}),
        ))
        .await
        .unwrap();
    assert_eq!(hub.history()["events"].as_array().unwrap().len(), 1);
    let content = std::fs::read(logs.join("twitch-moderation.log")).unwrap();
    assert!(content.starts_with(b"\xef\xbb\xbfLegacy\r\n"));
    std::fs::remove_file(logs.join("twitch-moderation.log")).unwrap();
    std::fs::create_dir(logs.join("twitch-moderation.log")).unwrap();
    let result = runtime
        .execute(action(json!({"action":"clear_chat"})))
        .await
        .unwrap();
    assert!(result.applied);
    assert!(result.warnings[0].to_lowercase().contains("protokoll"));
    assert!(hub.history()["events"].as_array().unwrap().is_empty());
    assert_eq!(runtime.snapshot().await.entries.len(), 2);
    assert!(runtime
        .export(&root.path().join("failed.txt"))
        .await
        .is_err());
}
