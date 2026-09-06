use super::*;
use tauri::test::{mock_builder, mock_context, noop_assets, MockRuntime};

fn test_app(root: PathBuf) -> tauri::App<MockRuntime> {
    let paths = AppPaths::from_root(root);
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let secrets = Arc::new(KeyringSecretStore::new());
    let hub = Arc::new(RealtimeHub::new());
    let bridge = OverlayEventBridge::new(hub.clone());
    let alerts = Arc::new(AlertEngine::from_store(settings.clone(), bridge.clone()));
    mock_builder()
        .manage(AppState {
            ytm: Mutex::new(None),
            settings_mutation: Mutex::new(()),
            obs: ObsClient::new_shared("127.0.0.1", 4455),
            twitch: TwitchClient::new_shared(secrets.clone()),
            spotify: SpotifyClient::new_shared(secrets.clone()),
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
            setup_overlay_source,
            open_overlay_editor,
            test_alert,
            delete_alert,
            alert_runtime,
            get_settings,
            save_settings,
            list_canvases,
            update_canvas
        ])
        .build(mock_context(noop_assets()))
        .unwrap()
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
            url: "http://tauri.localhost".parse().unwrap(),
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
