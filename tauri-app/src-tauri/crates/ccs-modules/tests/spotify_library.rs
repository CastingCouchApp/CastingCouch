use ccs_modules::spotify::{
    SpotifyAction, SpotifyClient, SpotifyOAuthClient, SpotifyQuery, SpotifyTokenRepository,
    SpotifyTokenSet,
};
use ccs_secrets::MemorySecretStore;
use serde_json::json;
use std::sync::Arc;
use wiremock::{
    matchers::{header, method, path, query_param},
    Mock, MockServer, ResponseTemplate,
};
async fn library() -> (SpotifyClient, MockServer) {
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
    (
        SpotifyClient::with_http(secrets, SpotifyOAuthClient::new(), server.uri()),
        server,
    )
}
#[tokio::test]
async fn library_matches_csharp_search_playlist_and_saved_track_api_contracts() {
    let (client, server) = library().await;
    Mock::given(method("GET"))
        .and(path("/search"))
        .and(query_param("q", "test + track"))
        .and(query_param("limit", "10"))
        .and(query_param("offset", "10"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(json!({"tracks":{"items":[],"next":null}})),
        )
        .expect(1)
        .mount(&server)
        .await;
    client
        .query(
            "client",
            SpotifyQuery::Search {
                text: "  test + track  ".into(),
            },
            Some(10),
        )
        .await
        .unwrap();
    Mock::given(method("GET"))
        .and(path("/playlists/list/items"))
        .and(query_param("offset", "50"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(
                json!({"items":[{"item":{"id":"song","type":"track"}}],"next":null}),
            ),
        )
        .expect(1)
        .mount(&server)
        .await;
    let items = client
        .query(
            "client",
            SpotifyQuery::PlaylistTracks { id: "list".into() },
            Some(50),
        )
        .await
        .unwrap();
    assert_eq!(items["items"][0]["item"]["id"], "song");
    for (verb, action) in [
        ("PUT", SpotifyAction::SaveTrack { id: "song".into() }),
        (
            "DELETE",
            SpotifyAction::RemoveSavedTrack {
                id: "spotify:track:song".into(),
            },
        ),
    ] {
        Mock::given(method(verb))
            .and(path("/me/library"))
            .and(query_param("uris", "spotify:track:song"))
            .and(header("authorization", "Bearer token"))
            .respond_with(ResponseTemplate::new(204))
            .expect(1)
            .mount(&server)
            .await;
        client.action("client", action).await.unwrap();
    }
    assert!(client
        .query(
            "client",
            SpotifyQuery::Search {
                text: "test".into()
            },
            Some(1001)
        )
        .await
        .is_err());
    let empty = client
        .query("client", SpotifyQuery::Search { text: "  ".into() }, None)
        .await
        .unwrap();
    assert_eq!(empty["tracks"]["items"], json!([]));
}
#[tokio::test]
async fn saved_status_and_recent_queue_do_not_receive_offset_parameters() {
    let (client, server) = library().await;
    Mock::given(method("GET"))
        .and(path("/me/library/contains"))
        .and(query_param("uris", "spotify:track:a,spotify:track:b"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!([true, false])))
        .expect(1)
        .mount(&server)
        .await;
    let status = client
        .query(
            "client",
            serde_json::from_value(json!({"query":"saved_status","ids":["a","spotify:track:b"]}))
                .unwrap(),
            Some(50),
        )
        .await
        .unwrap();
    assert_eq!(status, json!([true, false]));
    for (query, url) in [
        (SpotifyQuery::Recent, "/me/player/recently-played"),
        (SpotifyQuery::Queue, "/me/player/queue"),
    ] {
        Mock::given(method("GET"))
            .and(path(url))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({"items":[]})))
            .mount(&server)
            .await;
        client.query("client", query, Some(50)).await.unwrap();
    }
    assert!(server
        .received_requests()
        .await
        .unwrap()
        .iter()
        .all(|r| !r.url.query_pairs().any(|(key, _)| key == "offset")));
}
#[tokio::test]
async fn all_playlists_follow_pages_deduplicate_sort_and_stop_on_empty_page() {
    let (client, server) = library().await;
    Mock::given(method("GET")).and(path("/me/playlists")).and(query_param("offset","0")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"items":[{"id":"z","name":"Zulu"},{"id":"a","name":"Alpha"}],"next":"https://ignored.example/next"}))).mount(&server).await;
    Mock::given(method("GET"))
        .and(path("/me/playlists"))
        .and(query_param("offset", "2"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({"items":[{"id":"a","name":"duplicate"},{"id":"b","name":"Beta"}],"next":"next"}),
        ))
        .mount(&server)
        .await;
    Mock::given(method("GET"))
        .and(path("/me/playlists"))
        .and(query_param("offset", "4"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"items":[],"next":"next"})))
        .mount(&server)
        .await;
    let result = client
        .query(
            "client",
            serde_json::from_value(json!({"query":"all_playlists"})).unwrap(),
            None,
        )
        .await
        .unwrap();
    assert_eq!(
        result["items"]
            .as_array()
            .unwrap()
            .iter()
            .map(|i| i["name"].as_str().unwrap())
            .collect::<Vec<_>>(),
        vec!["Alpha", "Beta", "Zulu"]
    );
    assert!(result["next"].is_null());
}
