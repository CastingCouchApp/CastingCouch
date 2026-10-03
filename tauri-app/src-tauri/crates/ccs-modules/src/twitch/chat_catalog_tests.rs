use super::*;
use crate::overlay_bridge::OverlayEventBridge;
use crate::twitch::eventsub::{parse_eventsub_message, EventSubMessage};
use crate::twitch::TwitchHelixClient;
use ccs_core::settings::OverlayChatSettings;
use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_overlay_server::{OverlayServer, RealtimeHub};
use serde_json::{json, Value};
use std::sync::Arc;
use wiremock::{
    matchers::{header, method, path, query_param},
    Mock, MockServer, ResponseTemplate,
};

async fn response(server: &MockServer, route: &str, body: Value) {
    Mock::given(method("GET"))
        .and(path(route))
        .respond_with(ResponseTemplate::new(200).set_body_json(body))
        .mount(server)
        .await;
}
fn catalog(server: &MockServer) -> ChatCatalogs {
    ChatCatalogs::with_bases(
        format!("{}/bttv", server.uri()),
        format!("{}/ffz", server.uri()),
        format!("{}/7tv", server.uri()),
    )
}
fn helix(server: &MockServer) -> TwitchHelixClient {
    TwitchHelixClient::with_base_url(format!("{}/", server.uri()), "client", "token")
}
async fn fixtures(server: &MockServer) {
    response(
        server,
        "/bttv/emotes/global",
        json!([{"id":"global","code":"Same"},{"id":"b","code":"BTTV"}]),
    )
    .await;
    response(server,"/bttv/users/twitch/42",json!({"channelEmotes":[{"id":"channel","code":"Same"}],"sharedEmotes":[{"id":"s","code":"Shared"}]})).await;
    response(server,"/ffz/set/global",json!({"sets":{"a":{"emoticons":[{"name":"Same","urls":{"1":"//ffz/1","2":"//ffz/2"}},{"name":"FFZ","urls":{"1":"//ffz/only"}}]}}})).await;
    response(
        server,
        "/ffz/room/id/42",
        json!({"sets":{"a":{"emoticons":[{"name":"Same","urls":{"2":"https://ffz/channel"}}]}}}),
    )
    .await;
    response(server,"/7tv/emote-sets/global",json!({"emotes":[{"name":"Same","data":{"host":{"url":"//7tv/emote","files":[{"name":"1x.avif"},{"name":"2x.webp"}]}}}]})).await;
    response(server,"/7tv/users/twitch/42",json!({"emote_set":{"emotes":[{"name":"Seven","data":{"host":{"url":"https://7tv/channel/","files":[{"name":"only.webp"}]}}}]}})).await;
    Mock::given(method("GET")).and(path("/chat/badges/global")).and(header("Authorization","Bearer token")).and(header("Client-Id","client")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{"set_id":"subscriber","versions":[{"id":"12","image_url_2x":"https://badges/global","title":"Global"}]}]}))).mount(server).await;
    Mock::given(method("GET")).and(path("/chat/badges")).and(query_param("broadcaster_id","42")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{"set_id":"subscriber","versions":[{"id":"12","image_url_2x":"https://badges/channel","title":"Channel"}]}]}))).mount(server).await;
}
fn event(text: &str, fragments: Value) -> super::super::TwitchEvent {
    let raw = json!({"metadata":{"message_type":"notification"},"payload":{"subscription":{"type":"channel.chat.message"},"event":{"broadcaster_user_id":"42","chatter_user_name":"Alice","chatter_user_id":"a","message_id":"m","message":{"text":text,"fragments":fragments},"badges":[{"set_id":"subscriber","id":"12"},{"set_id":"moderator","id":"1"},{"set_id":"unknown","id":"1"}]}}});
    match parse_eventsub_message(&raw.to_string()).unwrap() {
        EventSubMessage::Notification(event) => event,
        _ => panic!(),
    }
}
fn parts(event: &super::super::TwitchEvent) -> Value {
    serde_json::from_str(&event.data["parts"]).unwrap()
}

#[tokio::test]
async fn csharp_provider_precedence_badges_and_fragments_reach_actual_overlay_http() {
    let server = MockServer::start().await;
    fixtures(&server).await;
    let catalog = catalog(&server);
    let settings = OverlayChatSettings::default();
    let status = catalog.refresh(&helix(&server), "42", &settings).await;
    assert!(status.errors.is_empty(), "{:?}", status.errors);
    assert_eq!(status.emotes, 5);
    assert_eq!(status.badges, 1);
    let mut event = event(
        "Hi Same\tBTTV FFZ Seven Shared Kappa",
        json!([
            {"type":"text","text":"Hi Same\tBTTV FFZ Seven Shared "},
            {"type":"emote","text":"Kappa","emote":{"id":"25"}},
            {"type":"mention","text":" Same"},{"type":"cheermote","text":" BTTV"}
        ]),
    );
    catalog.enrich(&mut event, &settings);
    let data = parts(&event);
    assert_eq!(data[1]["url"], "https://7tv/emote/2x.webp");
    assert_eq!(data[1]["provider"], "7tv");
    assert_eq!(data[2]["text"], "\t");
    assert_eq!(data[3]["url"], "https://cdn.betterttv.net/emote/b/2x.webp");
    assert_eq!(data[5]["url"], "https://ffz/only");
    assert_eq!(data[7]["url"], "https://7tv/channel/only.webp");
    assert_eq!(data[11]["provider"], "twitch");
    assert_eq!(data[12]["text"], " Same BTTV");
    let badges: Value = serde_json::from_str(&event.data["badges"]).unwrap();
    assert_eq!(badges.as_array().unwrap().len(), 2);
    assert_eq!(
        badges[0],
        json!({"setId":"subscriber","id":"12","url":"https://badges/channel","title":"Channel"})
    );
    assert!(badges[1]["url"].as_str().unwrap().contains("3267646d"));
    let root = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(root.path().into());
    let store = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let hub = Arc::new(RealtimeHub::new());
    let mut frames = hub.subscribe();
    let overlay = OverlayServer::start(store, paths, hub.clone(), 0)
        .await
        .unwrap();
    use futures_util::StreamExt;
    let (mut socket, _) =
        tokio_tungstenite::connect_async(format!("ws://127.0.0.1:{}/ws", overlay.port))
            .await
            .unwrap();
    let hello = tokio::time::timeout(std::time::Duration::from_secs(2), socket.next())
        .await
        .unwrap()
        .unwrap()
        .unwrap();
    assert_eq!(
        serde_json::from_str::<Value>(hello.to_text().unwrap()).unwrap()["type"],
        "app.ws.hello"
    );
    let bridge = OverlayEventBridge::new(hub);
    bridge.from_twitch(
        &event.event_type,
        &event.summary,
        event.received_at,
        event.data.clone(),
    );
    let frame: Value = serde_json::from_str(&frames.recv().await.unwrap()).unwrap();
    let delivered = tokio::time::timeout(std::time::Duration::from_secs(2), async {
        loop {
            let message = socket.next().await.unwrap().unwrap();
            if let Ok(value) = serde_json::from_str::<Value>(message.to_text().unwrap_or("")) {
                if value["type"] == "channel.chat.message" {
                    break value;
                }
            }
        }
    })
    .await
    .unwrap();
    assert_eq!(delivered, frame);
    let history: Value = reqwest::get(format!("http://127.0.0.1:{}/chat/history", overlay.port))
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(
        history["events"][0]["data"]["parts"],
        frame["data"]["parts"]
    );
    assert_eq!(
        history["events"][0]["data"]["badges"],
        frame["data"]["badges"]
    );
    overlay.stop();
}

#[tokio::test]
async fn one_failed_provider_does_not_block_badges_or_remaining_emotes() {
    let server = MockServer::start().await;
    fixtures(&server).await;
    for route in ["/ffz/set/global", "/ffz/room/id/42"] {
        Mock::given(method("GET"))
            .and(path(route))
            .respond_with(ResponseTemplate::new(503))
            .with_priority(1)
            .mount(&server)
            .await;
    }
    let catalog = catalog(&server);
    let settings = OverlayChatSettings::default();
    let status = catalog.refresh(&helix(&server), "42", &settings).await;
    assert_eq!(status.emotes, 4);
    assert_eq!(status.badges, 1);
    assert_eq!(status.errors.len(), 2);
    assert!(status.errors.iter().all(|error| error.starts_with("FFZ:")));
    let mut message = event(
        "Seven BTTV FFZ",
        json!([{"type":"text","text":"Seven BTTV FFZ"}]),
    );
    catalog.enrich(&mut message, &settings);
    assert_eq!(parts(&message)[0]["provider"], "7tv");
    assert_eq!(parts(&message)[2]["provider"], "bttv");
    assert_eq!(parts(&message)[3]["text"], " FFZ");
}

#[tokio::test]
async fn disabled_providers_stop_immediately_and_dont_receive_requests() {
    let server = MockServer::start().await;
    fixtures(&server).await;
    let catalog = catalog(&server);
    let mut settings = OverlayChatSettings::default();
    catalog.refresh(&helix(&server), "42", &settings).await;
    settings.enable_seven_tv = false;
    let mut message = event(
        "Same samE Same!",
        json!([{"type":"text","text":"Same samE Same!"}]),
    );
    catalog.enrich(&mut message, &settings);
    assert_eq!(parts(&message)[0]["url"], "https://ffz/channel");
    assert_eq!(parts(&message)[1]["text"], " samE Same!");
    server.reset().await;
    settings.enable_bttv = false;
    settings.enable_ffz = false;
    catalog.refresh(&helix(&server), "42", &settings).await;
    assert!(server
        .received_requests()
        .await
        .unwrap()
        .iter()
        .all(|request| request.url.path().starts_with("/chat/badges")));
    catalog.enrich(&mut message, &settings);
    assert_eq!(
        parts(&message),
        json!([{"type":"text","text":"Same samE Same!"}])
    );
}

#[tokio::test]
async fn isolated_provider_failures_keep_previous_catalog_only_for_the_same_channel() {
    let server = MockServer::start().await;
    fixtures(&server).await;
    let catalog = catalog(&server);
    let settings = OverlayChatSettings::default();
    catalog.refresh(&helix(&server), "42", &settings).await;
    server.reset().await;
    Mock::given(method("GET"))
        .respond_with(ResponseTemplate::new(503))
        .mount(&server)
        .await;
    let failed = catalog.refresh(&helix(&server), "42", &settings).await;
    assert!(!failed.errors.is_empty());
    assert_eq!(failed.emotes, 5);
    let other = catalog
        .refresh(&helix(&server), "different", &settings)
        .await;
    assert_eq!(other.emotes, 0);
    assert_eq!(other.badges, 0);
    let mut message = event("Same", json!([{"type":"text","text":"Same"}]));
    catalog.enrich(&mut message, &settings);
    assert_eq!(parts(&message), json!([{"type":"text","text":"Same"}]));
}

#[test]
fn badge_fallbacks_work_before_refresh_and_preserve_native_emotes() {
    let catalog = ChatCatalogs::new();
    let mut message = event(
        "Kappa",
        json!([{"type":"emote","text":"Kappa","emote":{"id":"25"}}]),
    );
    catalog.enrich(&mut message, &OverlayChatSettings::default());
    assert_eq!(parts(&message)[0]["provider"], "twitch");
    let badges: Value = serde_json::from_str(&message.data["badges"]).unwrap();
    assert_eq!(badges.as_array().unwrap().len(), 2);
    assert_eq!(badges[0]["title"], "Subscriber");
    assert_eq!(badges[1]["title"], "Moderator");
}

#[test]
fn badge_catalog_uses_one_x_fallback_and_any_version_case_insensitively() {
    let badges=parse_badges(&json!({"data":[{"set_id":"SUBSCRIBER","versions":[{"id":"9","image_url_2x":"","image_url_1x":"https://badges/one","title":""}]}]})).unwrap();
    assert_eq!(badges.versions.len(), 1);
    let catalog = ChatCatalogs::new();
    {
        let mut data = catalog.data.write().unwrap();
        data.status.channel_id = "42".into();
        data.channel_badges = badges;
    }
    let mut message = event("text", json!([{"type":"text","text":"text"}]));
    catalog.enrich(&mut message, &OverlayChatSettings::default());
    let resolved: Value = serde_json::from_str(&message.data["badges"]).unwrap();
    assert_eq!(resolved[0]["url"], "https://badges/one");
    assert_eq!(resolved[0]["id"], "12");
    assert_eq!(resolved[0]["title"], "SUBSCRIBER");
}

#[test]
fn badge_unknown_version_prefers_last_channel_definition_over_global_versions() {
    let catalog = ChatCatalogs::new();
    {
        let mut data = catalog.data.write().unwrap();
        data.status.channel_id = "42".into();
        data.global_badges=parse_badges(&json!({"data":[{"set_id":"subscriber","versions":[{"id":"99","image_url_2x":"https://badges/global"}]}]})).unwrap();
        data.channel_badges=parse_badges(&json!({"data":[{"set_id":"subscriber","versions":[{"id":"9","image_url_2x":"https://badges/channel9"},{"id":"2","image_url_2x":"https://badges/channel2"}]}]})).unwrap();
    }
    let mut message = event("hi", json!([{"type":"text","text":"hi"}]));
    catalog.enrich(&mut message, &OverlayChatSettings::default());
    let badges: Value = serde_json::from_str(&message.data["badges"]).unwrap();
    assert_eq!(badges[0]["url"], "https://badges/channel2");
}

#[tokio::test]
async fn runtime_refresh_is_cached_serialized_and_logout_clears_channel_data() {
    use super::super::{
        TwitchClient, TwitchHelixUser, TwitchOAuthClient, TwitchTokenRepository, TwitchTokenSet,
    };
    use ccs_secrets::MemorySecretStore;
    let server = MockServer::start().await;
    fixtures(&server).await;
    response(
        &server,
        "/validate",
        json!({"client_id":"client","login":"alice","user_id":"42","expires_in":3600,"scopes":[]}),
    )
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
    let mut client = TwitchClient::with_http(
        secrets,
        TwitchOAuthClient::with_base_urls(
            format!("{}/device", server.uri()),
            format!("{}/token", server.uri()),
            format!("{}/validate", server.uri()),
        ),
        format!("{}/", server.uri()),
    );
    client.chat_catalogs = catalog(&server);
    *client.current_user.write().await = Some(TwitchHelixUser {
        id: "42".into(),
        login: "alice".into(),
        display_name: "Alice".into(),
        profile_image_url: String::new(),
    });
    let settings = OverlayChatSettings::default();
    let (first, second) = tokio::join!(
        client.refresh_chat_catalogs("client", "", &settings, false),
        client.refresh_chat_catalogs("client", "", &settings, false)
    );
    assert_eq!(first.emotes, 5);
    assert_eq!(second.emotes, 5);
    assert_eq!(
        server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .filter(|r| r.url.path() == "/bttv/emotes/global")
            .count(),
        1
    );
    client.logout().await.unwrap();
    assert_eq!(client.chat_catalog_status().emotes, 0);
    let disconnected = client
        .refresh_chat_catalogs("client", "", &settings, true)
        .await;
    assert_eq!(disconnected.emotes, 0);
    assert!(!disconnected.errors.is_empty());
}
