use ccs_modules::twitch::{
    build_raid_suggestions, normalize_raid_channels, remember_raid_channel, RaidSuggestion,
};

fn suggestion(login: &str, live: bool, source: &str) -> RaidSuggestion {
    RaidSuggestion {
        login: login.into(),
        display_name: login.into(),
        is_live: live,
        source_label: source.into(),
    }
}
#[test]
fn raid_channels_match_csharp_normalization_and_recent_limit() {
    assert_eq!(
        normalize_raid_channels(&[" @Alpha ".into(), "alpha".into(), "".into(), "@Beta".into()]),
        ["Alpha", "Beta"]
    );
    let mut previous = (1..=45).map(|i| format!("channel{i}")).collect::<Vec<_>>();
    previous.push("TARGET".into());
    let recent = remember_raid_channel(&previous, "@target");
    assert_eq!(recent[0], "target");
    assert_eq!(recent.len(), 40);
    assert_eq!(
        recent
            .iter()
            .filter(|s| s.eq_ignore_ascii_case("target"))
            .count(),
        1
    );
}
#[test]
fn suggestions_match_csharp_recent_live_offline_priority_and_query_filter() {
    let suggestions = build_raid_suggestions(
        &["recent-offline".into(), "recent-live".into()],
        &[
            suggestion("followed-offline", false, "Gefolgt"),
            suggestion("duplicate-live", false, "Gefolgt"),
        ],
        &[
            suggestion("live-follow", true, "Live"),
            suggestion("duplicate-live", true, "Live"),
        ],
        &[
            suggestion("search-live", true, "Suche"),
            suggestion("search-offline", false, "Suche"),
        ],
        &[suggestion("recent-live", true, "Live")],
        "",
        25,
    );
    assert_eq!(
        suggestions
            .iter()
            .map(|s| s.login.as_str())
            .collect::<Vec<_>>(),
        [
            "recent-live",
            "recent-offline",
            "live-follow",
            "duplicate-live",
            "search-live",
            "followed-offline",
            "search-offline"
        ]
    );
    assert_eq!(suggestions[0].source_label, "Zuletzt");
    assert_eq!(
        build_raid_suggestions(&[], &suggestions, &[], &[], &[], "SEARCH", 1)[0].login,
        "search-live"
    );
}

#[tokio::test]
async fn community_http_pages_merges_live_probes_and_keeps_optional_errors_visible() {
    use ccs_modules::twitch::TwitchHelixClient;
    use serde_json::json;
    use wiremock::{
        matchers::{header, path, query_param},
        Mock, MockServer, ResponseTemplate,
    };
    let server = MockServer::start().await;
    for (cursor, body) in [
        (
            "",
            json!({"data":[{"broadcaster_login":"follow","broadcaster_name":"Follow"}],"pagination":{"cursor":"next"}}),
        ),
        (
            "next",
            json!({"data":[{"broadcaster_login":"other","broadcaster_name":"Other"}],"pagination":{}}),
        ),
    ] {
        let mock = Mock::given(path("/channels/followed"))
            .and(query_param("user_id", "owner"))
            .and(query_param("first", "100"))
            .and(header("Client-Id", "client"));
        let mock = if cursor.is_empty() {
            mock
        } else {
            mock.and(query_param("after", cursor))
        };
        mock.respond_with(ResponseTemplate::new(200).set_body_json(body))
            .with_priority(if cursor.is_empty() { 2 } else { 1 })
            .expect(1)
            .mount(&server)
            .await;
    }
    Mock::given(path("/streams/followed"))
        .respond_with(ResponseTemplate::new(403).set_body_json(json!({"message":"Missing scope"})))
        .mount(&server)
        .await;
    Mock::given(path("/streams")).and(query_param("user_login","recent")).and(query_param("user_login","follow")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{"user_login":"recent","user_name":"Recent"},{"user_login":"follow","user_name":"Follow"}]}))).expect(1).mount(&server).await;
    let helix = TwitchHelixClient::with_base_url(format!("{}/", server.uri()), "client", "token");
    let report = helix
        .raid_suggestions("owner", &["recent".into()], "")
        .await;
    assert_eq!(
        report
            .suggestions
            .iter()
            .map(|s| s.login.as_str())
            .collect::<Vec<_>>(),
        ["recent", "follow", "other"]
    );
    assert!(report.suggestions[1].is_live);
    assert!(report.warnings.iter().any(|s| s.contains("Missing scope")));
    assert_eq!(
        server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .filter(|r| r.url.path() == "/search/channels")
            .count(),
        0
    );
}

#[tokio::test]
async fn raid_preflight_rejects_missing_self_offline_and_posts_once_with_optional_chat() {
    use ccs_modules::twitch::TwitchHelixClient;
    use serde_json::json;
    use wiremock::{
        matchers::{body_json, method, path, query_param},
        Mock, MockServer, ResponseTemplate,
    };
    let server = MockServer::start().await;
    for (login, data) in [
        ("missing", json!([])),
        (
            "self",
            json!([{"id":"owner","login":"self","display_name":"Self"}]),
        ),
        (
            "offline",
            json!([{"id":"off","login":"offline","display_name":"Offline"}]),
        ),
        (
            "target",
            json!([{"id":"target-id","login":"target","display_name":"Target","profile_image_url":"https://image"}]),
        ),
    ] {
        Mock::given(path("/users"))
            .and(query_param("login", login))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":data})))
            .mount(&server)
            .await;
    }
    Mock::given(path("/streams"))
        .and(query_param("user_id", "off"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[]})))
        .mount(&server)
        .await;
    Mock::given(path("/streams")).and(query_param("user_id","target-id")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{"game_name":"Game","title":"Title","viewer_count":12,"started_at":"2026-10-03T10:00:00Z"}]}))).mount(&server).await;
    Mock::given(method("POST"))
        .and(path("/raids"))
        .and(query_param("from_broadcaster_id", "owner"))
        .and(query_param("to_broadcaster_id", "target-id"))
        .respond_with(
            ResponseTemplate::new(200)
                .set_body_json(json!({"data":[{"created_at":"now","is_mature":false}]})),
        )
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("POST"))
        .and(path("/chat/messages"))
        .and(body_json(
            json!({"broadcaster_id":"owner","sender_id":"user","message":"/raid target"}),
        ))
        .respond_with(ResponseTemplate::new(403).set_body_json(json!({"message":"No chat scope"})))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("DELETE"))
        .and(path("/raids"))
        .and(query_param("broadcaster_id", "owner"))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let helix = TwitchHelixClient::with_base_url(format!("{}/", server.uri()), "client", "token");
    assert!(helix
        .start_raid("owner", "user", "missing", true)
        .await
        .unwrap_err()
        .to_string()
        .contains("nicht gefunden"));
    assert!(helix
        .start_raid("owner", "user", "self", true)
        .await
        .unwrap_err()
        .to_string()
        .contains("eigenen"));
    assert!(helix
        .start_raid("owner", "user", "offline", true)
        .await
        .unwrap_err()
        .to_string()
        .contains("offline"));
    let result = helix
        .start_raid("owner", "user", " @target ", true)
        .await
        .unwrap();
    assert_eq!(result.target.viewer_count, 12);
    assert_eq!(result.target.category, "Game");
    assert!(result.warnings.iter().any(|s| s.contains("No chat scope")));
    helix.cancel_raid("owner").await.unwrap();
}

#[tokio::test]
async fn failed_raid_is_not_retried_and_does_not_echo_chat() {
    use ccs_modules::twitch::TwitchHelixClient;
    use serde_json::json;
    use wiremock::{
        matchers::{method, path},
        Mock, MockServer, ResponseTemplate,
    };
    let server = MockServer::start().await;
    Mock::given(path("/users"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({"data":[{"id":"target","login":"target","display_name":"Target"}]}),
        ))
        .mount(&server)
        .await;
    Mock::given(path("/streams"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{}]})))
        .mount(&server)
        .await;
    Mock::given(method("POST"))
        .and(path("/raids"))
        .respond_with(ResponseTemplate::new(429).set_body_json(json!({"message":"Too many raids"})))
        .expect(1)
        .mount(&server)
        .await;
    let helix = TwitchHelixClient::with_base_url(format!("{}/", server.uri()), "client", "token");
    assert!(helix
        .start_raid("owner", "user", "target", true)
        .await
        .unwrap_err()
        .to_string()
        .contains("429: Too many raids"));
    assert!(!server
        .received_requests()
        .await
        .unwrap()
        .iter()
        .any(|r| r.url.path() == "/chat/messages"));
}

#[tokio::test]
async fn ambiguous_raid_response_requires_manual_verification_and_pagination_cycle_is_visible() {
    use ccs_modules::twitch::TwitchHelixClient;
    use serde_json::json;
    use wiremock::{
        matchers::{method, path},
        Mock, MockServer, ResponseTemplate,
    };
    let server = MockServer::start().await;
    Mock::given(path("/users"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({"data":[{"id":"target","login":"target","display_name":"Target"}]}),
        ))
        .mount(&server)
        .await;
    Mock::given(path("/streams"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{}]})))
        .mount(&server)
        .await;
    Mock::given(method("POST"))
        .and(path("/raids"))
        .respond_with(
            ResponseTemplate::new(503).set_body_json(json!({"message":"Service unavailable"})),
        )
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(path("/channels/followed"))
        .respond_with(
            ResponseTemplate::new(200)
                .set_body_json(json!({"data":[],"pagination":{"cursor":"cycle"}})),
        )
        .expect(2)
        .mount(&server)
        .await;
    Mock::given(path("/streams/followed"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[]})))
        .mount(&server)
        .await;
    let helix = TwitchHelixClient::with_base_url(format!("{}/", server.uri()), "client", "token");
    let error = helix
        .start_raid("owner", "user", "target", false)
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("Raid-Ausgang unklar"));
    assert!(error.contains("503"));
    let report = helix.raid_suggestions("owner", &[], "").await;
    assert!(report
        .warnings
        .iter()
        .any(|s| s.contains("wiederholte Pagination")));
}
