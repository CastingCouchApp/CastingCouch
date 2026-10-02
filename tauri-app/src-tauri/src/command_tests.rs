use super::*;
use tauri::test::{mock_builder, mock_context, noop_assets, MockRuntime};

fn test_app(root: PathBuf) -> tauri::App<MockRuntime> {
    test_app_with_spotify(root, None)
}
fn test_app_with_spotify(
    root: PathBuf,
    spotify: Option<Arc<SpotifyClient>>,
) -> tauri::App<MockRuntime> {
    let paths = AppPaths::from_root(root);
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let secrets = Arc::new(KeyringSecretStore::new());
    let hub = Arc::new(RealtimeHub::new());
    let bridge = OverlayEventBridge::new(hub.clone());
    let alerts = Arc::new(AlertEngine::from_store(settings.clone(), bridge.clone()));
    mock_builder()
        .manage(StartupState::default())
        .manage(AppState {
            ytm: Mutex::new(None),
            settings_mutation: Mutex::new(()),
            obs: ObsClient::new_shared("127.0.0.1", 4455),
            twitch: TwitchClient::new_shared(secrets.clone()),
            spotify: spotify.unwrap_or_else(|| SpotifyClient::new_shared(secrets.clone())),
            paths,
            settings,
            secrets,
            hub,
            bridge,
            alerts,
            overlay: Mutex::new(None),
            verified_update: Mutex::new(None),
            _lock: None,
        })
        .invoke_handler(tauri::generate_handler![
            chat_history,
            twitch_action,
            twitch_query,
            activate_spotify_device,
            spotify_query,
            spotify_action,
            set_spotify_playlist_favorite,
            startup_error,
            overlay_runtime_status,
            setup_overlay_source,
            obs_query,
            open_overlay_editor,
            test_alert,
            upsert_alert,
            alert_preview,
            alert_queue_settings,
            delete_alert,
            alert_runtime,
            get_settings,
            save_settings,
            list_profiles,
            create_profile,
            update_profile,
            import_profile,
            export_profile,
            delete_profile,
            apply_profile,
            list_canvases,
            update_canvas
        ])
        .build(mock_context(noop_assets()))
        .unwrap()
}

#[test]
fn playlist_favorites_and_successful_playback_persist_through_native_ipc() {
    use ccs_modules::spotify::{SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet};
    use ccs_secrets::MemorySecretStore;
    use wiremock::{
        matchers::{method, path},
        Mock, MockServer, ResponseTemplate,
    };
    let dir = tempfile::tempdir().unwrap();
    let (spotify, server) = tauri::async_runtime::block_on(async {
        let server = MockServer::start().await;
        let secrets = Arc::new(MemorySecretStore::new());
        SpotifyTokenRepository::new(secrets.clone())
            .save(&SpotifyTokenSet {
                access_token: "token".into(),
                refresh_token: "refresh".into(),
                obtained_at: "2099-01-01T00:00:00Z".parse().unwrap(),
                expires_in_seconds: 3600,
                token_type: "Bearer".into(),
                scopes: vec![],
            })
            .unwrap();
        Mock::given(method("PUT"))
            .and(path("/me/player/play"))
            .respond_with(ResponseTemplate::new(204))
            .mount(&server)
            .await;
        Mock::given(method("PUT"))
            .and(path("/me/player/shuffle"))
            .respond_with(ResponseTemplate::new(204))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/me/library/contains"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!([true])))
            .mount(&server)
            .await;
        (
            Arc::new(SpotifyClient::with_http(
                secrets,
                SpotifyOAuthClient::new(),
                server.uri(),
            )),
            server,
        )
    });
    let app = test_app_with_spotify(dir.path().into(), Some(spotify));
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let mut original = call(&window, "get_settings", json!({})).unwrap();
    original["Spotify"]["AutoTransferToPreferredDevice"] = json!(false);
    original["Spotify"]["PreferredDeviceId"] = json!("preferred");
    original["Spotify"]["RecentPlaylistUris"] = json!([
        "spotify:playlist:old1",
        "spotify:playlist:old2",
        "spotify:playlist:old3",
        "spotify:playlist:old4",
        "spotify:playlist:old5"
    ]);
    let initial = call(&window, "get_settings", json!({})).unwrap();
    call(
        &window,
        "save_settings",
        json!({"original":initial,"settings":original}),
    )
    .unwrap();
    for _ in 0..2 {
        call(
            &window,
            "set_spotify_playlist_favorite",
            json!({"uri":"spotify:playlist:studio","favorite":true}),
        )
        .unwrap();
    }
    assert!(call(
        &window,
        "set_spotify_playlist_favorite",
        json!({"uri":"bad","favorite":true})
    )
    .is_err());
    call(
        &window,
        "spotify_action",
        json!({"action":{"action":"play_playlist","uri":"spotify:playlist:studio"}}),
    )
    .unwrap();
    assert_eq!(
        call(
            &window,
            "spotify_query",
            json!({"query":{"query":"saved_status","ids":["song"]}})
        )
        .unwrap(),
        json!([true])
    );
    let settings = call(&window, "get_settings", json!({})).unwrap();
    assert_eq!(
        settings["Spotify"]["FavoritePlaylistUris"],
        json!(["spotify:playlist:studio"])
    );
    assert_eq!(
        settings["Spotify"]["RecentPlaylistUris"],
        json!([
            "spotify:playlist:studio",
            "spotify:playlist:old1",
            "spotify:playlist:old2",
            "spotify:playlist:old3",
            "spotify:playlist:old4"
        ])
    );
    tauri::async_runtime::block_on(async {
        let calls = server.received_requests().await.unwrap();
        assert_eq!(calls[0].url.path(), "/me/player/play");
        assert_eq!(calls[1].url.path(), "/me/player/shuffle");
        server.reset().await;
    });
    assert!(call(
        &window,
        "spotify_action",
        json!({"action":{"action":"play_playlist","uri":"spotify:playlist:failed"}})
    )
    .is_err());
    assert_eq!(
        call(&window, "get_settings", json!({})).unwrap()["Spotify"]["RecentPlaylistUris"],
        settings["Spotify"]["RecentPlaylistUris"]
    );
    let restarted = test_app(dir.path().into());
    let restarted_window = WebviewWindowBuilder::new(&restarted, "main", Default::default())
        .build()
        .unwrap();
    assert_eq!(
        call(&restarted_window, "get_settings", json!({})).unwrap()["Spotify"]
            ["FavoritePlaylistUris"],
        settings["Spotify"]["FavoritePlaylistUris"]
    );
}
fn call(
    window: &tauri::WebviewWindow<MockRuntime>,
    cmd: &str,
    body: Value,
) -> Result<Value, Value> {
    tauri::test::get_ipc_response(
        window,
        tauri::webview::InvokeRequest {
            cmd: cmd.into(),
            callback: tauri::ipc::CallbackFn(0),
            error: tauri::ipc::CallbackFn(1),
            url: window.url().unwrap(),
            body: tauri::ipc::InvokeBody::Json(body),
            headers: Default::default(),
            invoke_key: tauri::test::INVOKE_KEY.into(),
        },
    )
    .map(|body| body.deserialize::<Value>().unwrap())
}
#[test]
fn real_ipc_decodes_camel_case_and_persists_commands() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().to_path_buf());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    assert!(call(&window, "test_alert", json!({"alert_type":"Follow"})).is_err());
    assert_eq!(
        call(
            &window,
            "test_alert",
            json!({"alertType":"Follow","user":"Contract"})
        )
        .unwrap(),
        1
    );
    assert_eq!(
        call(
            &window,
            "alert_runtime",
            json!({"enabled":false,"obsSceneName":"Contract alerts"})
        )
        .unwrap()["obs_scene_name"],
        "Contract alerts"
    );
    call(&window, "delete_alert", json!({"alertType":"Follow"})).unwrap();
    let disk: Value =
        serde_json::from_slice(&std::fs::read(dir.path().join("settings.json")).unwrap()).unwrap();
    assert!(disk["Alerts"]["Definitions"].get("Follow").is_none());
    assert_eq!(disk["Alerts"]["ObsSceneName"], "Contract alerts");
    call(
        &window,
        "update_canvas",
        json!({"id":"default","name":"Renamed","selected":true}),
    )
    .unwrap();
    assert_eq!(
        call(&window, "list_canvases", json!({})).unwrap()[0]["name"],
        "Renamed"
    );
}

#[test]
fn alert_designer_preview_rename_and_queue_settings_cross_real_ipc() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().to_path_buf());
    let window = WebviewWindowBuilder::new(&app, "alerts", Default::default())
        .build()
        .unwrap();
    let state = app.state::<AppState>();
    let before = tauri::async_runtime::block_on(state.alerts.list()).unwrap();
    let mut definition = before
        .into_iter()
        .find(|alert| alert.type_name == "ReSub")
        .unwrap();
    definition.type_name = "New ReSub".into();
    let payload = serde_json::to_value(&definition).unwrap();
    assert!(call(
        &window,
        "upsert_alert",
        json!({"alert":payload,"original_type":"ReSub"})
    )
    .is_ok());
    // Unknown snake_case optional arguments cannot rename an existing definition.
    assert!(tauri::async_runtime::block_on(state.alerts.list())
        .unwrap()
        .iter()
        .any(|alert| alert.type_name == "ReSub"));
    definition.type_name = "Renamed".into();
    let payload = serde_json::to_value(&definition).unwrap();
    call(
        &window,
        "upsert_alert",
        json!({"alert":payload,"originalType":"ReSub"}),
    )
    .unwrap();
    assert!(!tauri::async_runtime::block_on(state.alerts.list())
        .unwrap()
        .iter()
        .any(|alert| alert.type_name == "ReSub"));
    call(
        &window,
        "alert_queue_settings",
        json!({"capacity":12,"delayMilliseconds":750}),
    )
    .unwrap();
    assert_eq!(
        tauri::async_runtime::block_on(state.settings.load())
            .unwrap()
            .alerts
            .inter_alert_delay_milliseconds,
        750
    );
    assert!(call(
        &window,
        "alert_queue_settings",
        json!({"capacity":0,"delayMilliseconds":750})
    )
    .is_err());
    let preview = call(&window,"alert_preview",json!({"alert":AlertDefinition{type_name:"ReSub".into(),text_template:"{USER}: {months}".into(),..Default::default()},"user":"Tester"})).unwrap();
    assert_eq!(preview["text"], "Tester: 12");
}

#[test]
fn disabling_alerts_via_general_settings_stops_the_running_worker() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().to_path_buf());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    call(
        &window,
        "test_alert",
        json!({"alertType":"Follow","user":"Tester"}),
    )
    .unwrap();
    let state = app.state::<AppState>();
    tauri::async_runtime::block_on(async {
        tokio::time::timeout(std::time::Duration::from_secs(2), async {
            while state.alerts.runtime().await.unwrap().current_type.is_none() {
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
    });
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut edited = original.clone();
    edited["Alerts"]["Enabled"] = json!(false);
    call(
        &window,
        "save_settings",
        json!({"original":original,"settings":edited}),
    )
    .unwrap();
    tauri::async_runtime::block_on(async {
        tokio::time::timeout(std::time::Duration::from_secs(2), async {
            while state.alerts.pending_count() != 0 {
                tokio::task::yield_now().await;
            }
        })
        .await
        .expect("general settings must stop the active alert");
    });
}
#[test]
fn occupied_port_fails_through_ipc_without_saving_settings() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().to_path_buf());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let occupied = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut edited = original.clone();
    edited["Overlay"]["WebServerPort"] = json!(occupied.local_addr().unwrap().port());
    assert!(call(
        &window,
        "save_settings",
        json!({"original":original,"settings":edited})
    )
    .is_err());
    assert_eq!(call(&window, "get_settings", json!({})).unwrap(), original);
}

#[test]
fn editor_url_is_decoded_by_the_native_command_handler() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().to_path_buf());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    assert!(call(&window,"open_overlay_editor",json!({"id":"contract","name":"Canvas","editor_url":"http://127.0.0.1:8765/editor/contract"})).is_err());
    call(&window,"open_overlay_editor",json!({"id":"contract","name":"Canvas","editorUrl":"http://127.0.0.1:8765/editor/contract"})).unwrap();
    assert!(app.get_webview_window("overlay-editor-contract").is_some());
}

#[test]
fn source_setup_contract_rejects_unknown_canvas_before_touching_obs() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().to_path_buf());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    assert_eq!(
        call(
            &window,
            "setup_overlay_source",
            json!({"canvasId":"absent","sceneName":"Live","inputName":"Canvas"})
        )
        .unwrap_err(),
        json!("Canvas existiert nicht")
    );
}

#[test]
fn obs_queries_use_typed_camel_case_arguments_through_ipc() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().to_path_buf());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    assert_eq!(
        call(
            &window,
            "obs_query",
            json!({"query":{"query":"scene_items","sceneName":""}})
        )
        .unwrap_err(),
        json!("OBS-Abfrage benötigt einen gültigen Namen.")
    );
    let error = call(
        &window,
        "obs_query",
        json!({"query":{"query":"audio_monitor","input_name":"Mic"}}),
    )
    .unwrap_err();
    assert!(error.as_str().unwrap().contains("inputName"));
}

#[test]
fn successful_port_change_replaces_listener_and_reports_actual_status() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let before = call(&window, "get_settings", json!({})).unwrap();
    let reserved = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = reserved.local_addr().unwrap().port();
    drop(reserved);
    let mut edited = before.clone();
    edited["Overlay"]["WebServerPort"] = json!(port);
    let result = call(
        &window,
        "save_settings",
        json!({"settings":edited,"original":before}),
    )
    .unwrap();
    assert_eq!(result["saved"], true);
    assert_eq!(result["warnings"], json!([]));
    assert_eq!(
        call(&window, "overlay_runtime_status", json!({})).unwrap()["port"],
        port
    );
    tauri::async_runtime::block_on(async {
        let response = reqwest::get(format!("http://127.0.0.1:{port}/health"))
            .await
            .unwrap();
        assert!(response.status().is_success());
        app.state::<AppState>()
            .overlay
            .lock()
            .await
            .as_ref()
            .unwrap()
            .stop();
    });
    assert_eq!(
        call(&window, "overlay_runtime_status", json!({})).unwrap()["running"],
        false
    );
}
#[test]
fn startup_failure_is_readable_without_service_initialization() {
    let app = mock_builder()
        .manage(StartupState(std::sync::Mutex::new(Some(
            "settings unavailable".into(),
        ))))
        .invoke_handler(tauri::generate_handler![startup_error])
        .build(mock_context(noop_assets()))
        .unwrap();
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    assert_eq!(
        call(&window, "startup_error", json!({})).unwrap(),
        "settings unavailable"
    );
}

#[test]
fn disabled_twitch_chat_rejects_send_before_credentials_or_network() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    tauri::async_runtime::block_on(async {
        let state = app.state::<AppState>();
        let mut settings = state.settings.load().await.unwrap();
        settings.twitch.enable_chat = false;
        state.settings.save(&settings).await.unwrap();
        state
            .hub
            .publish(&json!({"type":"channel.chat.message","data":{"messageId":"contract"}}));
    });
    assert_eq!(
        call(&window, "chat_history", json!({})).unwrap()["events"],
        json!([])
    );
    assert_eq!(
        call(
            &window,
            "twitch_action",
            json!({"action":{"action":"send_chat","message":"hello"}})
        )
        .unwrap_err(),
        "Twitch-Chat ist deaktiviert."
    );
}

#[test]
fn twitch_management_arguments_and_validation_cross_native_ipc() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    for (action, error) in [
        (
            json!({"action":"update_reward","id":"r","title":" ","isPaused":true,"isUserInputRequired":false,"backgroundColor":"#123456"}),
            "Reward-Titel",
        ),
        (json!({"action":"delete_reward","id":" "}), "Reward-ID"),
        (
            json!({"action":"end_prediction","id":"p","status":"RESOLVED","winningOutcomeId":null}),
            "Gewinnendes Ergebnis",
        ),
    ] {
        assert!(call(&window, "twitch_action", json!({"action":action}))
            .unwrap_err()
            .as_str()
            .unwrap()
            .contains(error));
    }
    assert_eq!(call(&window,"twitch_query",json!({"query":{"query":"redemptions","rewardId":"r","status":"ACTIVE"},"after":"cursor"})).unwrap_err(),"Ungültiger Twitch-Status");
}

#[test]
fn spotify_preferences_persist_through_ipc_and_restart() {
    let dir = tempfile::tempdir().unwrap();
    {
        let app = test_app(dir.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let original = call(&window, "get_settings", json!({})).unwrap();
        let mut next = original.clone();
        next["Spotify"]["PreferredDeviceId"] = json!("studio");
        next["Spotify"]["UseActiveDeviceWhenPreferredUnavailable"] = json!(false);
        next["Spotify"]["AutoTransferToPreferredDevice"] = json!(true);
        call(
            &window,
            "save_settings",
            json!({"original":original,"settings":next}),
        )
        .unwrap();
        let error = call(
            &window,
            "activate_spotify_device",
            json!({"play":"invalid"}),
        )
        .unwrap_err();
        assert!(error.as_str().unwrap().contains("boolean"));
    }
    let app = test_app(dir.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let loaded = call(&window, "get_settings", json!({})).unwrap();
    assert_eq!(loaded["Spotify"]["PreferredDeviceId"], "studio");
    assert_eq!(
        loaded["Spotify"]["UseActiveDeviceWhenPreferredUnavailable"],
        false
    );
    assert_eq!(loaded["Spotify"]["AutoTransferToPreferredDevice"], true);
}

#[test]
fn profiles_cross_native_ipc_and_failed_apply_preserves_current_settings() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let original = call(&window, "get_settings", json!({})).unwrap();
    let profile = call(
        &window,
        "create_profile",
        json!({"name":"Studio","description":"Test"}),
    )
    .unwrap();
    let id = profile["Id"].as_str().unwrap();
    assert_eq!(
        call(&window, "list_profiles", json!({})).unwrap()["profiles"][0]["name"],
        "Studio"
    );
    let export = dir.path().join("saved.ccsprofile");
    call(&window, "export_profile", json!({"id":id,"path":export})).unwrap();
    let imported = call(&window, "import_profile", json!({"path":export})).unwrap();
    assert_ne!(imported["Id"], profile["Id"]);
    let mut next = original.clone();
    next["Branding"]["DisplayName"] = json!("Changed");
    call(
        &window,
        "save_settings",
        json!({"original":original,"settings":next}),
    )
    .unwrap();
    assert!(call(
        &window,
        "apply_profile",
        json!({"id":id,"original":original})
    )
    .is_err());
    assert_eq!(
        call(&window, "get_settings", json!({})).unwrap()["Branding"]["DisplayName"],
        "Changed"
    );
    let result = call(&window, "apply_profile", json!({"id":id,"original":next})).unwrap();
    assert_eq!(result["saved"], true);
    assert_eq!(
        call(&window, "get_settings", json!({})).unwrap()["Branding"]["DisplayName"],
        original["Branding"]["DisplayName"]
    );
    call(&window, "delete_profile", json!({"id":id})).unwrap();
    assert_eq!(
        call(&window, "list_profiles", json!({})).unwrap()["profiles"]
            .as_array()
            .unwrap()
            .len(),
        1
    );
}

#[test]
fn profile_apply_rejects_occupied_port_and_restarts_server_on_success() {
    let dir = tempfile::tempdir().unwrap();
    let app = test_app(dir.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut profile = call(
        &window,
        "create_profile",
        json!({"name":"Port","description":""}),
    )
    .unwrap();
    let id = profile["Id"].as_str().unwrap().to_string();
    let file = dir.path().join("Profiles").join(format!("{id}.json"));
    let occupied = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = occupied.local_addr().unwrap().port();
    profile["Settings"]["Overlay"]["WebServerPort"] = json!(port);
    std::fs::write(&file, serde_json::to_vec(&profile).unwrap()).unwrap();
    assert!(call(
        &window,
        "apply_profile",
        json!({"id":id,"original":original})
    )
    .is_err());
    assert_eq!(call(&window, "get_settings", json!({})).unwrap(), original);
    drop(occupied);
    assert_eq!(
        call(
            &window,
            "apply_profile",
            json!({"id":id,"original":original})
        )
        .unwrap()["saved"],
        true
    );
    assert_eq!(
        call(&window, "overlay_runtime_status", json!({})).unwrap()["port"],
        port
    );
    tauri::async_runtime::block_on(async {
        assert!(reqwest::get(format!("http://127.0.0.1:{port}/health"))
            .await
            .unwrap()
            .status()
            .is_success());
        app.state::<AppState>()
            .overlay
            .lock()
            .await
            .as_ref()
            .unwrap()
            .stop();
    });
    let restarted = test_app(dir.path().into());
    let restarted_window = WebviewWindowBuilder::new(&restarted, "main", Default::default())
        .build()
        .unwrap();
    assert_eq!(
        call(&restarted_window, "get_settings", json!({})).unwrap()["Overlay"]["WebServerPort"],
        port
    );
}
