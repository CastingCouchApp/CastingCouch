use super::*;

#[test]
fn native_service_launch_uses_saved_path_reports_errors_and_does_not_connect() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let missing = call(&window, "launch_service", json!({"service":"obs"})).unwrap_err();
        assert!(missing.to_string().contains("Programmpfad"));
        assert!(call(&window, "launch_service", json!({"service":"streamerbot"})).is_err());
        assert_eq!(
            call(
                &window,
                "notifications_snapshot",
                json!({"filter":"Fehler"})
            )
            .unwrap()["total"],
            2
        );

        let folder = root.path().join("Programme mit Leerzeichen ä");
        std::fs::create_dir(&folder).unwrap();
        let source = folder.join("helper.rs");
        let exe = folder.join(if cfg!(windows) {
            "ccslaunchfixture.exe"
        } else {
            "ccslaunchfixture"
        });
        std::fs::write(&source, r#"fn main() {
            let root = std::env::current_exe().unwrap().parent().unwrap().to_path_buf();
            let mut out = std::fs::OpenOptions::new().create(true).append(true).open(root.join("starts.txt")).unwrap();
            use std::io::Write;
            writeln!(out, "{}|{}", std::env::current_dir().unwrap().display(), std::env::args().count()).unwrap();
            let deadline=std::time::Instant::now()+std::time::Duration::from_secs(10);
            while !root.join("stop").exists() && std::time::Instant::now()<deadline { std::thread::sleep(std::time::Duration::from_millis(20)); }
        }"#).unwrap();
        let compiled = std::process::Command::new("rustc")
            .arg(&source)
            .arg("-o")
            .arg(&exe)
            .output()
            .unwrap();
        assert!(
            compiled.status.success(),
            "{}",
            String::from_utf8_lossy(&compiled.stderr)
        );
        let state = app.state::<AppState>();
        let mut settings = state.settings.read_value().await.unwrap();
        settings["Obs"]["ExecutablePath"] = json!(exe);
        let original = state.settings.read_value().await.unwrap();
        state
            .settings
            .save_edit(&original, &settings)
            .await
            .unwrap();
        let first = call(&window, "launch_service", json!({"service":"obs"})).unwrap();
        assert_eq!(first["status"], "started");
        let started = folder.join("starts.txt");
        tokio::time::timeout(std::time::Duration::from_secs(5), async {
            while !started.exists() {
                tokio::time::sleep(std::time::Duration::from_millis(20)).await;
            }
        })
        .await
        .unwrap();
        let again = call(&window, "launch_service", json!({"service":"obs"})).unwrap();
        let starts = std::fs::read_to_string(&started).unwrap();
        std::fs::write(folder.join("stop"), "stop").unwrap();
        tokio::time::sleep(std::time::Duration::from_millis(200)).await;
        assert_eq!(again["status"], "already_running");
        assert_eq!(starts.lines().count(), 1);
        assert!(starts.trim_end().ends_with("|1"));
        assert_eq!(
            std::fs::canonicalize(starts.trim_end().trim_end_matches("|1")).unwrap(),
            std::fs::canonicalize(&folder).unwrap()
        );
        assert_eq!(
            state.obs.status().await.state,
            ccs_modules::ConnectionState::Disconnected
        );
        assert_eq!(
            call(&window, "notifications_snapshot", json!({"filter":"Info"})).unwrap()["entries"]
                .as_array()
                .unwrap()
                .len(),
            2
        );
    });
}

#[test]
fn native_notification_commands_preserve_csharp_data_and_report_failed_edits() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let file = root.path().join("notifications.json");
        std::fs::write(&file, json!([{"Timestamp":"2026-10-03T12:00:00+02:00","Severity":"Warnung","Message":"Legacy notification","IsRead":false,"Future":42}]).to_string()).unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let journal = || call(&window, "notifications_snapshot", json!({"filter":"Alle"})).unwrap();
        assert_eq!(journal()["entries"][0]["message"], "Legacy notification");
        assert!(call(
            &window,
            "notifications_snapshot",
            json!({"filter":"invalid"})
        )
        .is_err());
        call(&window, "notifications_mark_read", json!({})).unwrap();
        assert_eq!(journal()["unreadCount"], 0);
        let saved: Value = serde_json::from_slice(&std::fs::read(&file).unwrap()).unwrap();
        assert_eq!(saved[0]["Future"], 42);
        std::fs::remove_file(&file).unwrap();
        std::fs::create_dir(&file).unwrap();
        assert!(call(&window, "notifications_clear", json!({})).is_err());
        assert_eq!(journal()["total"], 1);
        assert!(!journal()["warnings"].as_array().unwrap().is_empty());
        std::fs::remove_dir(&file).unwrap();
        call(&window, "notifications_retry", json!({})).unwrap();
        assert!(journal()["warnings"].as_array().unwrap().is_empty());
        call(&window, "notifications_clear", json!({})).unwrap();
        assert_eq!(journal()["total"], 0);
        assert_eq!(
            ccs_modules::notifications::NotificationRuntime::new(root.path())
                .snapshot("Alle")
                .unwrap()
                .total,
            0
        );
    });
}

#[test]
fn native_notifications_receive_events_and_preflight_failures_without_interrupting_checks() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let changes = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let received = changes.clone();
        let listener = app.listen("notifications-changed", move |_| {
            received.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        });
        runtime::spawn_notification_events(app.handle().clone(), state.notifications.clone());
        stream_end_host::spawn_events(app.handle().clone(), state.stream_end.clone());
        let (tx, rx) = broadcast::channel(8);
        spawn_status_forward(app.handle().clone(), rx);
        tx.send(ServiceStatus {
            id: "obs".into(),
            name: "OBS".into(),
            state: ccs_modules::ConnectionState::Error,
            detail: "native socket failure".into(),
        })
        .unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(3), async {
            while state
                .notifications
                .snapshot("Fehler")
                .unwrap()
                .entries
                .is_empty()
            {
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
        assert_eq!(
            call(
                &window,
                "notifications_snapshot",
                json!({"filter":"Fehler"})
            )
            .unwrap()["entries"][0]["message"],
            "OBS: Verbindungsfehler: native socket failure"
        );
        call(&window, "dashboard_preflight", json!({})).unwrap();
        let journal = call(&window, "notifications_snapshot", json!({"filter":"Alle"})).unwrap();
        assert!(journal["entries"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["message"] == "Preflight gestartet."));
        assert!(journal["entries"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["severity"] == "Warnung"));
        seed_unresolved_stream_end(&state).await;
        tokio::time::timeout(std::time::Duration::from_secs(3), async {
            while !state
                .notifications
                .snapshot("Warnungen")
                .unwrap()
                .entries
                .iter()
                .any(|e| e.message.contains("Twitch API 403"))
            {
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(3), async {
            while changes.load(std::sync::atomic::Ordering::SeqCst) == 0 {
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
        let file = root.path().join("notifications.json");
        std::fs::remove_file(&file).unwrap();
        std::fs::create_dir(&file).unwrap();
        assert_eq!(
            call(&window, "dashboard_preflight", json!({})).unwrap()["checks"]
                .as_array()
                .unwrap()
                .len(),
            9
        );
        assert!(
            !call(&window, "notifications_snapshot", json!({"filter":"Alle"})).unwrap()["warnings"]
                .as_array()
                .unwrap()
                .is_empty()
        );
        std::fs::write(&state.paths.settings_file, "invalid settings").unwrap();
        assert!(call(&window, "dashboard_preflight", json!({})).is_err());
        assert!(call(
            &window,
            "notifications_snapshot",
            json!({"filter":"Fehler"})
        )
        .unwrap()["entries"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["message"]
                .as_str()
                .unwrap()
                .starts_with("Preflight fehlgeschlagen:")));
        app.unlisten(listener);
    });
}

struct PendingRaidTestIo;
impl ccs_modules::stream_end::StreamEndIo for PendingRaidTestIo {
    fn execute(
        &self,
        operation: ccs_modules::stream_end::StreamEndOperation,
    ) -> ccs_modules::stream_end::IoFuture<'_> {
        Box::pin(async move {
            use ccs_modules::stream_end::*;
            match operation {
                StreamEndOperation::ProbeRaid { .. } => {
                    Ok(StreamEndReply::Target(Some(RaidIdentity {
                        id: "target-id".into(),
                        login: "target".into(),
                        display_name: "Target".into(),
                        online: true,
                    })))
                }
                StreamEndOperation::CancelRaid => Err("Twitch API 403".into()),
                _ => Ok(StreamEndReply::Done),
            }
        })
    }
}
async fn seed_unresolved_stream_end(state: &AppState) {
    use ccs_modules::stream_end::*;
    state
        .stream_end
        .start(
            StreamEndPlan {
                preferences: StreamEndPreferences {
                    mode: "EndSceneRaidThenStop".into(),
                    selected_raid_channel: "target".into(),
                    ..Default::default()
                },
                end_scene: String::new(),
                start_scene: String::new(),
                broadcaster_id: "owner".into(),
                broadcaster_login: "owner".into(),
                play_end_music: false,
                pause_music_on_stream_end: false,
            },
            0,
            Arc::new(PendingRaidTestIo),
        )
        .await
        .unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        while state.stream_end.snapshot().await.phase != "raid_countdown" {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    state.stream_end.control("abort").await.unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        while state.stream_end.snapshot().await.active {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    assert!(state.stream_end.snapshot().await.raid_pending);
}

#[test]
fn unresolved_assistant_raid_blocks_both_native_start_paths_before_authorization() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        seed_unresolved_stream_end(&app.state::<AppState>()).await;
        for (cmd, args) in [
            ("start_twitch_raid", json!({"login":"target"})),
            (
                "twitch_action",
                json!({"action":{"action":"raid","id":"target-id"}}),
            ),
        ] {
            let error = call(&window, cmd, args).unwrap_err();
            assert!(
                error.as_str().unwrap().contains("Vorherigen Raid"),
                "{error}"
            );
        }
    });
}

#[test]
fn stream_end_preferences_cross_native_ipc_preserve_parallel_settings_and_restart_idle() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let initial = call(&window, "stream_end_snapshot", json!({})).unwrap();
        assert_eq!(initial["draft"]["mode"], "EndSceneThenStop");
        assert_eq!(
            call(&window, "stream_end_status", json!({})).unwrap()["phase"],
            "idle"
        );
        let mut original = initial["original"].clone();
        original["Branding"]["DisplayName"] = json!("parallel");
        call(
            &window,
            "save_settings",
            json!({"original":initial["original"],"settings":original}),
        )
        .unwrap();
        let mut draft = initial["draft"].clone();
        draft["endSceneSeconds"] = json!(3);
        draft["plannedSeconds"] = json!(600);
        let saved = call(
            &window,
            "save_stream_end_preferences",
            json!({"original":initial["original"],"draft":draft}),
        )
        .unwrap();
        assert_eq!(saved["original"]["Branding"]["DisplayName"], "parallel");
        assert_eq!(saved["original"]["Twitch"]["EndSceneDurationSeconds"], 3);
        assert_eq!(saved["original"]["Workflow"]["EndSceneSeconds"], 3);
        draft["endSceneSeconds"] = json!(4);
        assert!(call(
            &window,
            "save_stream_end_preferences",
            json!({"original":initial["original"],"draft":draft})
        )
        .is_err());
        assert!(call(&window, "start_stream_end", json!({"planned":false})).is_err());
        let restarted = test_app(root.path().into());
        let next = WebviewWindowBuilder::new(&restarted, "main", Default::default())
            .build()
            .unwrap();
        assert_eq!(
            call(&next, "stream_end_snapshot", json!({})).unwrap()["draft"]["plannedSeconds"],
            600
        );
        assert_eq!(
            call(&next, "stream_end_status", json!({})).unwrap()["phase"],
            "idle"
        );
    });
}

#[test]
fn dashboard_commands_persist_layout_keep_parallel_settings_and_report_conflicts() {
    use tauri::Listener;
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let settings = call(&window, "get_settings", json!({})).unwrap();
        let mut seed = settings.clone();
        seed["Dashboard"] = json!({"ModuleOrder":["Workflow","StreamControl"],"Future":{"keep":1},"SceneButtons":[{"Id":"stable","Title":"Live","SceneName":"Live","IconKind":"Emoji","IconValue":"🎮","Future":2}]});
        call(
            &window,
            "save_settings",
            json!({"original":settings,"settings":seed}),
        )
        .unwrap();
        let snapshot = call(&window, "dashboard_snapshot", json!({})).unwrap();
        let mut draft = snapshot["draft"].clone();
        draft["sceneButtons"][0]["title"] = json!("Gaming");
        draft["cards"][0]["size"] = json!("Groß");
        let mut parallel = seed.clone();
        parallel["Branding"]["DisplayName"] = json!("Parallel");
        call(
            &window,
            "save_settings",
            json!({"original":seed,"settings":parallel}),
        )
        .unwrap();
        let (changed, mut received) = tokio::sync::mpsc::unbounded_channel();
        app.listen("dashboard-changed", move |e| {
            let _ = changed.send(e.payload().to_string());
        });
        let saved = call(
            &window,
            "save_dashboard",
            json!({"original":snapshot["original"],"draft":draft}),
        )
        .unwrap();
        assert_eq!(saved["original"]["Branding"]["DisplayName"], "Parallel");
        assert_eq!(
            saved["original"]["Dashboard"]["SceneButtons"][0]["Future"],
            2
        );
        assert_eq!(saved["original"]["Dashboard"]["Future"]["keep"], 1);
        tokio::time::timeout(std::time::Duration::from_secs(1), received.recv())
            .await
            .unwrap()
            .unwrap();
        let mut conflicting = snapshot["draft"].clone();
        conflicting["sceneButtons"][0]["title"] = json!("Different");
        assert!(call(
            &window,
            "save_dashboard",
            json!({"original":snapshot["original"],"draft":conflicting})
        )
        .is_err());
        let disk = call(&window, "get_settings", json!({})).unwrap();
        assert_eq!(disk["Dashboard"]["SceneButtons"][0]["Title"], "Gaming");
        let mut invalid = saved["draft"].clone();
        invalid["cards"][0]["key"] = json!("Workflow");
        assert!(call(
            &window,
            "save_dashboard",
            json!({"original":saved["original"],"draft":invalid})
        )
        .is_err());
        let image = root.path().join("icon.png");
        std::fs::write(&image, b"\x89PNG\r\n\x1a\nbody").unwrap();
        assert!(
            call(&window, "dashboard_image_preview", json!({"path":image}))
                .unwrap()
                .as_str()
                .unwrap()
                .starts_with("data:image/png")
        );
        let library =
            ccs_overlay_server::MediaLibrary::new(&app.state::<AppState>().paths.overlay_root);
        let asset = library
            .import_image("scene.png", b"\x89PNG\r\n\x1a\nbody")
            .unwrap();
        library.import_image("shape.svg", b"<svg/>").unwrap();
        let choices = call(&window, "dashboard_asset_choices", json!({})).unwrap();
        assert_eq!(choices.as_array().unwrap().len(), 1);
        assert_eq!(choices[0]["id"], asset["id"]);
        assert!(std::path::Path::new(choices[0]["path"].as_str().unwrap()).is_file());
        drop(window);
        drop(app);
        let second = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&second, "main", Default::default())
            .build()
            .unwrap();
        let restored = call(&window, "dashboard_snapshot", json!({})).unwrap();
        assert_eq!(restored["draft"]["sceneButtons"][0]["id"], "stable");
        assert_eq!(restored["draft"]["sceneButtons"][0]["title"], "Gaming");
        assert_eq!(restored["draft"]["cards"][0]["size"], "Groß");
    });
}

#[test]
fn creator_intelligence_commands_cross_native_ipc_journal_and_persistent_mutations() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        assert!(call(
            &window,
            "record_creator_note",
            json!({"note":"Not active","requestId":"1"})
        )
        .is_err());
        let at = chrono::Utc::now() - chrono::Duration::hours(1);
        let state = app.state::<AppState>();
        state.stream_history.observe(&json!({"stream":{"available":true,"isLive":true,"elapsedSeconds":0},"obs":{"currentScene":"Main"}}),&json!({}),&json!({}),at).unwrap();
        call(
            &window,
            "record_creator_note",
            json!({"note":"  Interview  ","requestId":"2"}),
        )
        .unwrap();
        call(
            &window,
            "record_creator_note",
            json!({"note":"  Interview  ","requestId":"2"}),
        )
        .unwrap();
        let live = call(
            &window,
            "creator_intelligence_snapshot",
            json!({"lookbackDays":7}),
        )
        .unwrap();
        assert_eq!(live["recording"], true);
        assert_eq!(live["dashboard"]["SessionCount"], 0);
        state
            .stream_history
            .observe_stream_event(false, chrono::Utc::now())
            .unwrap();
        let saved = call(
            &window,
            "creator_intelligence_snapshot",
            json!({"lookbackDays":30}),
        )
        .unwrap();
        assert_eq!(saved["dashboard"]["SessionCount"], 1);
        assert_eq!(saved["recording"], false);
        let action = saved["actions"]["Items"]
            .as_array()
            .unwrap()
            .iter()
            .find(|r| r["Metric"] == "engagement")
            .unwrap();
        let id = action["Id"].as_str().unwrap();
        call(&window, "start_creator_experiment", json!({"actionId":id})).unwrap();
        call(&window, "complete_creator_action", json!({"actionId":id})).unwrap();
        let report = call(&window, "generate_creator_weekly_report", json!({})).unwrap();
        assert!(std::fs::read_to_string(report.as_str().unwrap())
            .unwrap()
            .contains("Creator Intelligence Wochenbericht"));
        let history = call(&window, "stream_history_snapshot", json!({})).unwrap();
        assert_eq!(
            history["events"]
                .as_array()
                .unwrap()
                .iter()
                .filter(|r| r["Type"] == "session.note")
                .count(),
            1
        );
        drop(state);
        drop(window);
        drop(app);
        let second = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&second, "main", Default::default())
            .build()
            .unwrap();
        let restored = call(
            &window,
            "creator_intelligence_snapshot",
            json!({"lookbackDays":30}),
        )
        .unwrap();
        assert_eq!(restored["experiments"]["Rows"].as_array().unwrap().len(), 1);
        assert_eq!(
            restored["actions"]["Items"]
                .as_array()
                .unwrap()
                .iter()
                .find(|r| r["Id"] == id)
                .unwrap()["Status"],
            "Erledigt"
        );
        assert!(call(
            &window,
            "creator_intelligence_snapshot",
            json!({"lookbackDays":0})
        )
        .is_err());
        assert!(call(
            &window,
            "complete_creator_action",
            json!({"actionId":"missing"})
        )
        .is_err());
    });
}
#[test]
fn native_obs_commands_capture_immediate_session_events_and_forward_history_changes() {
    use futures_util::{SinkExt, StreamExt};
    use tauri::Listener;
    use tokio_tungstenite::tungstenite::Message;
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let (changed, mut received) = tokio::sync::mpsc::unbounded_channel();
        let listener = app.listen("stream-history-changed", move |event| {
            let _ = changed.send(event.payload().to_string());
        });
        runtime::spawn_stream_history_events(app.handle().clone(), state.stream_history.clone());
        let server = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = server.local_addr().unwrap().port();
        let requests = Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let server_requests = requests.clone();
        let task = tokio::spawn(async move {
            let (socket, _) = server.accept().await.unwrap();
            let mut ws = tokio_tungstenite::accept_async(socket).await.unwrap();
            ws.send(Message::Text(
                json!({"op":0,"d":{"obsWebSocketVersion":"5.6.0","rpcVersion":1}})
                    .to_string()
                    .into(),
            ))
            .await
            .unwrap();
            ws.next().await.unwrap().unwrap();
            ws.send(Message::Text(
                json!({"op":2,"d":{"negotiatedRpcVersion":1}})
                    .to_string()
                    .into(),
            ))
            .await
            .unwrap();
            let mut streaming = false;
            while let Some(Ok(Message::Text(text))) = ws.next().await {
                let request: Value = serde_json::from_str(&text).unwrap();
                let kind = request["d"]["requestType"].as_str().unwrap();
                server_requests.lock().unwrap().push(kind.to_string());
                if matches!(kind, "StartStream" | "StopStream") {
                    streaming = kind == "StartStream";
                    ws.send(Message::Text(json!({"op":5,"d":{"eventType":"StreamStateChanged","eventData":{"outputActive":kind=="StartStream","outputState":if kind=="StartStream" {"OBS_WEBSOCKET_OUTPUT_STARTED"}else{"OBS_WEBSOCKET_OUTPUT_STOPPED"}}}}).to_string().into())).await.unwrap();
                }
                let data = if kind == "GetSceneList" {
                    json!({"currentProgramSceneName":"Live","scenes":[]})
                } else if kind == "GetVideoSettings" {
                    json!({"baseWidth":1920,"baseHeight":1080})
                } else if kind == "GetSourceScreenshot" {
                    assert_eq!(request["d"]["requestData"]["sourceName"], "Live");
                    assert_eq!(request["d"]["requestData"]["imageWidth"], 960);
                    json!({"imageData":"data:image/png;base64,iVBORw0KGgo="})
                } else if kind == "GetStreamStatus" {
                    json!({"outputActive":streaming,"outputDuration":1000})
                } else {
                    json!({})
                };
                ws.send(Message::Text(json!({"op":7,"d":{"requestId":request["d"]["requestId"],"requestType":kind,"requestStatus":{"result":true,"code":100},"responseData":data}}).to_string().into())).await.unwrap();
            }
        });
        state
            .obs
            .connect_simple("127.0.0.1", port, None, false)
            .await
            .unwrap();
        let preview = call(&window, "dashboard_obs_preview", json!({})).unwrap();
        assert_eq!(preview["width"], 1920);
        assert_eq!(preview["height"], 1080);
        assert_eq!(preview["url"], "data:image/png;base64,iVBORw0KGgo=");
        call(
            &window,
            "obs_control",
            json!({"control":{"action":"start_stream"}}),
        )
        .unwrap();
        assert!(call(&window, "stream_history_snapshot", json!({})).unwrap()["active"].is_object());
        state.bridge.from_twitch(
            "channel.subscription.message",
            "ReSub",
            chrono::Utc::now(),
            Default::default(),
        );
        call(
            &window,
            "obs_control",
            json!({"control":{"action":"stop_stream"}}),
        )
        .unwrap();
        let snapshot = call(&window, "stream_history_snapshot", json!({})).unwrap();
        assert!(snapshot["active"].is_null());
        assert_eq!(snapshot["sessions"][0]["NewSubscriptions"], 1);
        // The actual assistant crosses Tauri IPC and the real OBS v5 client.
        let original = call(&window, "get_settings", json!({})).unwrap();
        let mut configured = original.clone();
        configured["Obs"]["EndScene"] = json!("End");
        configured["Obs"]["StartScene"] = json!("Start");
        configured["Spotify"]["PauseOnStreamEnd"] = json!(false);
        configured["Twitch"]["EndSceneDurationSeconds"] = json!(1);
        call(
            &window,
            "save_settings",
            json!({"original":original,"settings":configured}),
        )
        .unwrap();
        stream_end_host::spawn_events(app.handle().clone(), state.stream_end.clone());
        call(
            &window,
            "obs_control",
            json!({"control":{"action":"start_stream"}}),
        )
        .unwrap();
        call(
            &window,
            "start_stream_end",
            json!({"planned":true,"seconds":30}),
        )
        .unwrap();
        assert_eq!(
            call(&window, "stream_end_status", json!({})).unwrap()["phase"],
            "scheduled"
        );
        let blocked = call(&window, "start_twitch_raid", json!({"login":"target"})).unwrap_err();
        assert!(blocked.as_str().unwrap().contains("Assistent"));
        call(&window, "stream_end_control", json!({"action":"abort"})).unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(2), async {
            while state.stream_end.snapshot().await.active {
                tokio::time::sleep(std::time::Duration::from_millis(5)).await;
            }
        })
        .await
        .unwrap();
        assert_eq!(
            requests
                .lock()
                .unwrap()
                .iter()
                .filter(|kind| kind.as_str() == "StopStream")
                .count(),
            1
        );
        call(&window, "start_stream_end", json!({"planned":false})).unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(4), async {
            while state.stream_end.snapshot().await.active {
                tokio::time::sleep(std::time::Duration::from_millis(5)).await;
            }
        })
        .await
        .unwrap();
        let end = call(&window, "stream_end_status", json!({})).unwrap();
        assert_eq!(end["phase"], "completed", "{end}");
        assert_eq!(
            state.obs.current_program_scene().await.as_deref(),
            Some("Start")
        );
        assert_eq!(
            requests
                .lock()
                .unwrap()
                .iter()
                .filter(|kind| kind.as_str() == "StopStream")
                .count(),
            2
        );
        assert!(call(&window, "stream_history_snapshot", json!({})).unwrap()["active"].is_null());
        let notification = tokio::time::timeout(std::time::Duration::from_secs(2), received.recv())
            .await
            .unwrap()
            .unwrap();
        assert_eq!(
            serde_json::from_str::<Value>(&notification).unwrap(),
            json!({"changed":true})
        );
        state.obs.disconnect().await.unwrap();
        assert!(call(&window, "dashboard_obs_preview", json!({})).is_err());
        task.await.unwrap();
        app.unlisten(listener);
    });
}

#[test]
fn music_history_is_independent_of_overlay_output_and_ignores_pause_cover_and_duplicate_events() {
    use chrono::TimeZone;
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let at = chrono::Utc.with_ymd_and_hms(2026, 10, 3, 10, 0, 0).unwrap();
        state
            .stream_history
            .observe(
                &json!({"stream":{"available":true,"isLive":true},"obs":{"currentScene":"Live"}}),
                &json!({"connected":true,"viewerCount":{"value":7,"at":at.to_rfc3339()}}),
                &json!({}),
                at,
            )
            .unwrap();
        let mut settings = state.settings.load().await.unwrap();
        settings.spotify.extra["OverlayEnabled"] = json!(false);
        let mut music = ccs_modules::music_player::MusicPlayerSnapshot {
            provider: "spotify".into(),
            connected: true,
            title: "Song".into(),
            artist: "Artist".into(),
            album: "Album".into(),
            is_playing: true,
            ..Default::default()
        };
        let data = runtime::update_music_data(&state, &music, &settings);
        assert_eq!(data["overlayEnabled"], false);
        music.is_playing = false;
        music.cover_url = "cover-changed".into();
        runtime::update_music_data(&state, &music, &settings);
        state
            .bridge
            .app_music_track("spotify", "Song", "Artist", "cover-changed");
        let snapshot = call(&window, "stream_history_snapshot", json!({})).unwrap();
        let events = snapshot["events"]
            .as_array()
            .unwrap()
            .iter()
            .filter(|e| e["Type"] == "spotify.track.changed")
            .collect::<Vec<_>>();
        assert_eq!(events.len(), 1);
        assert_eq!(
            events[0]["Payload"],
            json!({"title":"Song","artist":"Artist","album":"Album","isPlaying":true,"scene":"Live","viewers":7})
        );
        music.album = "Another album".into();
        runtime::update_music_data(&state, &music, &settings);
        music.connected = false;
        music.title = "Stale".into();
        runtime::update_music_data(&state, &music, &settings);
        let snapshot = call(&window, "stream_history_snapshot", json!({})).unwrap();
        assert_eq!(
            snapshot["events"]
                .as_array()
                .unwrap()
                .iter()
                .filter(|e| e["Type"] == "spotify.track.changed")
                .count(),
            2
        );
    });
}

#[test]
fn stream_history_commands_read_capture_export_and_restore_across_native_ipc() {
    use chrono::TimeZone;
    use std::collections::BTreeMap;
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let at = chrono::Utc.with_ymd_and_hms(2026, 10, 3, 10, 0, 0).unwrap();
        let data = json!({"stream":{"available":true,"isLive":true,"elapsedSeconds":0},"obs":{"currentScene":"Live"}});
        let metrics = json!({"connected":true,"viewerCount":{"value":7,"at":at.to_rfc3339(),"error":null},"followers":{"value":0,"error":null},"title":"A <script>","category":"Game","channelError":null});
        state.hub.live.merge_snapshot(&data);
        state
            .stream_history
            .observe(&data, &metrics, &json!({}), at)
            .unwrap();
        state.bridge.from_twitch(
            "channel.chat.message",
            "Hallo",
            at,
            BTreeMap::from([
                ("messageId".into(), "m".into()),
                ("userName".into(), "Alice".into()),
            ]),
        );
        let active = call(&window, "stream_history_snapshot", json!({})).unwrap();
        assert_eq!(active["active"]["ChatMessages"], 1);
        state
            .stream_history
            .observe(
                &json!({"stream":{"available":true,"isLive":false}}),
                &metrics,
                &json!({}),
                at + chrono::Duration::seconds(60),
            )
            .unwrap();
        let saved = call(&window, "stream_history_snapshot", json!({})).unwrap();
        assert_eq!(saved["sessions"][0]["DurationSeconds"], 60);
        assert_eq!(saved["statistics"]["totalStreams"], 1);
        let session = saved["sessions"][0]["SessionId"].as_str().unwrap();
        let events = call(
            &window,
            "stream_history_snapshot",
            json!({"sessionId":session}),
        )
        .unwrap();
        assert!(events["events"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["Type"] == "twitch.chat.message" && e["Payload"]["user"] == "Alice"));
        let csv = root.path().join("export.csv");
        call(
            &window,
            "export_stream_history",
            json!({"format":"csv","path":csv}),
        )
        .unwrap();
        assert!(std::fs::read_to_string(csv)
            .unwrap()
            .contains("StartedAt;EndedAt"));
        let html = root.path().join("report.html");
        call(
            &window,
            "export_stream_history",
            json!({"format":"html","path":html}),
        )
        .unwrap();
        let report = std::fs::read_to_string(html).unwrap();
        assert!(report.contains("A &lt;script&gt;"));
        assert!(!report.contains("A <script>"));
        assert!(call(&window, "latest_stream_summary", json!({}))
            .unwrap()
            .as_str()
            .unwrap()
            .contains("Chatnachrichten: 1"));
        call(&window, "retry_stream_history", json!({})).unwrap();
        let restarted = test_app(root.path().into());
        let second = WebviewWindowBuilder::new(&restarted, "main", Default::default())
            .build()
            .unwrap();
        assert_eq!(
            call(&second, "stream_history_snapshot", json!({})).unwrap()["sessions"],
            saved["sessions"]
        );
        assert!(call(
            &second,
            "export_stream_history",
            json!({"format":"csv","path":root.path().join("StreamHistory/history.jsonl")})
        )
        .is_err());
    });
}

#[test]
fn raid_native_commands_preflight_start_cancel_emit_and_share_cached_suggestions() {
    use ccs_modules::twitch::{
        TwitchConnectOptions, TwitchOAuthClient, TwitchTokenRepository, TwitchTokenSet,
    };
    use ccs_secrets::MemorySecretStore;
    use wiremock::{
        matchers::{method, path, query_param},
        Mock, MockServer, ResponseTemplate,
    };
    tauri::async_runtime::block_on(async {
        let server = MockServer::start().await;
        let client_id = "abcdefghijabcdefghijabcdefghij";
        Mock::given(path("/validate")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"client_id":client_id,"login":"owner","user_id":"owner","scopes":[],"expires_in":3600}))).mount(&server).await;
        Mock::given(path("/users"))
            .respond_with(ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"owner","login":"owner","display_name":"Owner"}]}),
            ))
            .with_priority(2)
            .mount(&server)
            .await;
        Mock::given(path("/users"))
            .and(query_param("login", "target"))
            .respond_with(ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"target-id","login":"target","display_name":"Target"}]}),
            ))
            .with_priority(1)
            .mount(&server)
            .await;
        Mock::given(path("/streams")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{"user_login":"target","user_name":"Target","title":"Live","viewer_count":7}]}))).mount(&server).await;
        Mock::given(path("/channels/followed"))
            .respond_with(ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"broadcaster_login":"target","broadcaster_name":"Target"}]}),
            ))
            .expect(3)
            .mount(&server)
            .await;
        Mock::given(path("/streams/followed"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[]})))
            .expect(3)
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/raids"))
            .and(query_param("from_broadcaster_id", "owner"))
            .and(query_param("to_broadcaster_id", "target-id"))
            .respond_with(
                ResponseTemplate::new(200).set_body_json(json!({"data":[{"created_at":"now"}]})),
            )
            .expect(1)
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/chat/messages"))
            .respond_with(
                ResponseTemplate::new(403).set_body_json(json!({"message":"No chat permission"})),
            )
            .expect(1)
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
                client_id: client_id.into(),
                channel_name: String::new(),
                scopes: vec![],
                enable_event_sub: false,
            })
            .await
            .unwrap();
        let root = tempfile::tempdir().unwrap();
        let app = test_app_with_clients(root.path().into(), None, Some(twitch));
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let mut settings = state.settings.load().await.unwrap();
        settings.twitch.client_id = client_id.into();
        settings.twitch.enable_chat = true;
        state.settings.save(&settings).await.unwrap();
        let events = Arc::new(std::sync::Mutex::new(Vec::<Value>::new()));
        let received = events.clone();
        app.listen("twitch-raids-changed", move |e| {
            received
                .lock()
                .unwrap()
                .push(serde_json::from_str(e.payload()).unwrap())
        });
        for _ in 0..2 {
            let suggestions = call(
                &window,
                "twitch_raid_suggestions",
                json!({"query":"","force":false}),
            )
            .unwrap();
            assert_eq!(suggestions["suggestions"][0]["login"], "target");
        }
        let target = call(&window, "twitch_raid_target", json!({"login":"@target"})).unwrap();
        call(
            &window,
            "twitch_raid_suggestions",
            json!({"query":"","force":true}),
        )
        .unwrap();
        settings.twitch.channel_name = "owner".into();
        state.settings.save(&settings).await.unwrap();
        call(
            &window,
            "twitch_raid_suggestions",
            json!({"query":"","force":false}),
        )
        .unwrap();
        settings.twitch.channel_name = String::new();
        state.settings.save(&settings).await.unwrap();
        assert_eq!(target["viewerCount"], 7);
        let started = call(&window, "start_twitch_raid", json!({"login":"target"})).unwrap();
        assert!(started["warnings"][0]
            .as_str()
            .unwrap()
            .contains("No chat permission"));
        assert_eq!(
            call(&window, "twitch_raid_state", json!({})).unwrap()["requestedTarget"],
            "target"
        );
        assert!(call(&window, "start_twitch_raid", json!({"login":"target"})).is_err());
        assert!(call(
            &window,
            "twitch_action",
            json!({"action":{"action":"raid","id":"target-id"}})
        )
        .is_err());
        settings = state.settings.load().await.unwrap();
        settings.twitch.channel_name = "owner".into();
        state.settings.save(&settings).await.unwrap();
        assert!(
            call(&window, "twitch_raid_state", json!({})).unwrap()["requestedTarget"].is_null()
        );
        settings.twitch.channel_name = String::new();
        state.settings.save(&settings).await.unwrap();
        assert_eq!(
            call(&window, "twitch_raid_settings", json!({})).unwrap()["selected"],
            "target"
        );
        let denied = Mock::given(method("DELETE"))
            .and(path("/raids"))
            .respond_with(
                ResponseTemplate::new(403)
                    .set_body_json(json!({"message":"No cancellation permission"})),
            )
            .mount_as_scoped(&server)
            .await;
        assert!(call(&window, "cancel_twitch_raid", json!({})).is_err());
        assert_eq!(
            call(&window, "twitch_raid_state", json!({})).unwrap()["requestedTarget"],
            "target"
        );
        drop(denied);
        Mock::given(method("DELETE"))
            .and(path("/raids"))
            .respond_with(ResponseTemplate::new(204))
            .expect(1)
            .mount(&server)
            .await;
        call(&window, "cancel_twitch_raid", json!({})).unwrap();
        assert!(
            call(&window, "twitch_raid_state", json!({})).unwrap()["requestedTarget"].is_null()
        );
        assert!(events.lock().unwrap().len() >= 3);
        let ambiguous = Mock::given(method("POST"))
            .and(path("/raids"))
            .respond_with(
                ResponseTemplate::new(503).set_body_json(json!({"message":"Service unavailable"})),
            )
            .with_priority(1)
            .expect(1)
            .mount_as_scoped(&server)
            .await;
        assert!(
            call(&window, "start_twitch_raid", json!({"login":"target"}))
                .unwrap_err()
                .as_str()
                .unwrap()
                .contains("Raid-Ausgang unklar")
        );
        let uncertain = call(&window, "twitch_raid_state", json!({})).unwrap();
        assert_eq!(uncertain["requestedTarget"], "target");
        assert!(uncertain["lastError"].as_str().unwrap().contains("503"));
        assert!(call(&window, "start_twitch_raid", json!({"login":"target"})).is_err());
        drop(ambiguous);
        call(&window, "acknowledge_twitch_raid", json!({})).unwrap();
        assert!(
            call(&window, "twitch_raid_state", json!({})).unwrap()["requestedTarget"].is_null()
        );
        // An assistant's unresolved raid belongs to its original broadcaster,
        // even when the independent community state was reset by a channel edit.
        Mock::given(path("/users"))
            .and(query_param("login", "other"))
            .respond_with(ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"other-id","login":"other","display_name":"Other"}]}),
            ))
            .with_priority(1)
            .mount(&server)
            .await;
        seed_unresolved_stream_end(&state).await;
        settings.twitch.channel_name = "other".into();
        state.settings.save(&settings).await.unwrap();
        let before = server
            .received_requests()
            .await
            .unwrap()
            .iter()
            .filter(|r| r.method == "DELETE")
            .count();
        for (command, args) in [
            ("cancel_twitch_raid", json!({})),
            ("twitch_action", json!({"action":{"action":"cancel_raid"}})),
            ("acknowledge_twitch_raid", json!({})),
        ] {
            let error = call(&window, command, args).unwrap_err();
            assert!(
                error.as_str().unwrap().contains("gehört zu owner"),
                "{error}"
            );
            assert!(state.stream_end.snapshot().await.raid_pending);
        }
        assert_eq!(
            server
                .received_requests()
                .await
                .unwrap()
                .iter()
                .filter(|r| r.method == "DELETE")
                .count(),
            before
        );
        settings.twitch.channel_name = String::new();
        state.settings.save(&settings).await.unwrap();
        let cancelled_attempts = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let attempts = cancelled_attempts.clone();
        Mock::given(method("DELETE"))
            .and(path("/raids"))
            .and(query_param("broadcaster_id", "owner"))
            .respond_with(move |_request: &wiremock::Request| {
                ResponseTemplate::new(
                    if attempts.fetch_add(1, std::sync::atomic::Ordering::SeqCst) == 0 {
                        403
                    } else {
                        204
                    },
                )
            })
            .with_priority(1)
            .expect(2)
            .mount(&server)
            .await;
        assert!(call(
            &window,
            "twitch_action",
            json!({"action":{"action":"cancel_raid"}})
        )
        .is_err());
        assert!(state.stream_end.snapshot().await.raid_pending);
        call(
            &window,
            "twitch_action",
            json!({"action":{"action":"cancel_raid"}}),
        )
        .unwrap();
        assert!(!state.stream_end.snapshot().await.raid_pending);
        assert_eq!(
            cancelled_attempts.load(std::sync::atomic::Ordering::SeqCst),
            2
        );
        seed_unresolved_stream_end(&state).await;
        call(&window, "acknowledge_twitch_raid", json!({})).unwrap();
        assert!(!state.stream_end.snapshot().await.raid_pending);
    });
}

#[test]
fn raid_settings_commands_merge_concurrent_changes_preserve_unknown_fields_and_survive_restart() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let mut initial = state.settings.read_value().await.unwrap();
        initial["Twitch"]["FutureRaidOption"] = json!({"keep":true});
        let settings: AppSettings = serde_json::from_value(initial).unwrap();
        state.settings.save(&settings).await.unwrap();
        let snapshot = call(&window, "twitch_raid_settings", json!({})).unwrap();
        let mut parallel = state.settings.load().await.unwrap();
        parallel.branding.display_name = "Parallel".into();
        state.settings.save(&parallel).await.unwrap();
        let saved=call(&window,"save_twitch_raid_settings",json!({"channels":[" @Alpha ","alpha","@Beta",""],"selected":"@Beta","original":snapshot["original"]})).unwrap();
        assert_eq!(saved["channels"], json!(["Alpha", "Beta"]));
        assert_eq!(saved["selected"], "Beta");
        assert_eq!(saved["original"]["Branding"]["DisplayName"], "Parallel");
        assert_eq!(
            saved["original"]["Twitch"]["FutureRaidOption"]["keep"],
            true
        );
        assert!(call(
            &window,
            "save_twitch_raid_settings",
            json!({"channels":["different"],"selected":"different","original":snapshot["original"]})
        )
        .is_err());
        let remembered = call(
            &window,
            "select_twitch_raid_target",
            json!({"login":"@Beta"}),
        )
        .unwrap();
        assert_eq!(remembered["channels"], json!(["Beta", "Alpha"]));
        assert!(call(
            &window,
            "select_twitch_raid_target",
            json!({"login":"https://twitch.tv/foo"})
        )
        .is_err());
        let restarted = test_app(root.path().into());
        let second = WebviewWindowBuilder::new(&restarted, "main", Default::default())
            .build()
            .unwrap();
        assert_eq!(
            call(&second, "twitch_raid_settings", json!({})).unwrap()["selected"],
            "Beta"
        );
        assert_eq!(
            call(&second, "twitch_raid_state", json!({})).unwrap()["requestedTarget"],
            Value::Null
        );
    });
}
use tauri::test::{mock_builder, mock_context, noop_assets, MockRuntime};
use tauri::Listener;

#[test]
fn native_metric_refresh_uses_helix_and_saving_goals_uses_confirmed_zeroes() {
    use ccs_modules::twitch::{TwitchOAuthClient, TwitchTokenRepository, TwitchTokenSet};
    use ccs_secrets::MemorySecretStore;
    use wiremock::{matchers::path, Mock, MockServer, ResponseTemplate};
    tauri::async_runtime::block_on(async {
        let server = MockServer::start().await;
        let client_id = "abcdefghijabcdefghijabcdefghij";
        Mock::given(path("/validate")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"client_id":client_id,"login":"owner","user_id":"10","scopes":[],"expires_in":3600}))).mount(&server).await;
        Mock::given(path("/users"))
            .respond_with(ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"10","login":"owner","display_name":"Owner"}]}),
            ))
            .mount(&server)
            .await;
        for (endpoint, body) in [
            (
                "/channels",
                json!({"data":[{"title":"Title","game_name":"Game"}]}),
            ),
            ("/streams", json!({"data":[{"viewer_count":8}]})),
            ("/channels/followers", json!({"total":0})),
            ("/subscriptions", json!({"total":0})),
            ("/chat/chatters", json!({"total":3})),
        ] {
            Mock::given(path(endpoint))
                .respond_with(ResponseTemplate::new(200).set_body_json(body))
                .expect(if endpoint == "/channels" { 2 } else { 1 })
                .mount(&server)
                .await;
        }
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
        let app = test_app_with_clients(root.path().into(), None, Some(twitch));
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let mut settings = state.settings.load().await.unwrap();
        settings.twitch.client_id = client_id.into();
        settings.twitch.extra = json!({"FollowerGoal":{"Current":99,"Target":200},"SubGoal":{"Current":99,"Target":25}});
        settings.spotify.extra["OverlayEnabled"] = json!(false);
        state.settings.save(&settings).await.unwrap();
        let file = ccs_core::paths::overlay_data_path(&state.paths, &settings);
        std::fs::create_dir_all(file.parent().unwrap()).unwrap();
        std::fs::write(
            &file,
            json!({"music":{"title":"External"},"custom":{"keep":true}}).to_string(),
        )
        .unwrap();
        let updates = Arc::new(std::sync::Mutex::new(Vec::<Value>::new()));
        let received = updates.clone();
        app.listen("twitch-metrics-changed", move |event| {
            received
                .lock()
                .unwrap()
                .push(serde_json::from_str(event.payload()).unwrap())
        });
        let metrics = call(&window, "refresh_twitch_metrics", json!({})).unwrap();
        assert_eq!(metrics["viewerCount"]["value"], 8);
        assert_eq!(metrics["followers"]["value"], 0);
        assert_eq!(
            call(&window, "twitch_metrics_snapshot", json!({})).unwrap(),
            metrics
        );
        assert_eq!(updates.lock().unwrap().as_slice(), &[metrics]);
        let preflight = call(&window, "dashboard_preflight", json!({})).unwrap();
        let checks = preflight["checks"].as_array().unwrap();
        assert_eq!(checks.len(), 9);
        assert!(checks.iter().find(|c| c["key"] == "title").unwrap()["ok"]
            .as_bool()
            .unwrap());
        assert!(
            checks.iter().find(|c| c["key"] == "category").unwrap()["ok"]
                .as_bool()
                .unwrap()
        );
        Mock::given(path("/channels"))
            .respond_with(
                ResponseTemplate::new(403).set_body_json(json!({"message":"Channel denied"})),
            )
            .with_priority(1)
            .expect(1)
            .mount(&server)
            .await;
        let denied = call(&window, "dashboard_preflight", json!({})).unwrap();
        assert_eq!(
            denied["checks"]
                .as_array()
                .unwrap()
                .iter()
                .find(|c| c["key"] == "title")
                .unwrap()["ok"],
            false
        );
        assert!(denied.to_string().contains("Channel denied"));
        let initial = call(&window, "twitch_goals_snapshot", json!({})).unwrap();
        let result = call(
            &window,
            "save_twitch_goals",
            json!({"draft":initial["draft"],"original":initial["original"]}),
        )
        .unwrap();
        assert_eq!(
            result["original"]["Twitch"]["FollowerGoal"]["Current"].as_f64(),
            Some(0.0)
        );
        assert_eq!(
            result["original"]["Twitch"]["SubGoal"]["Current"].as_f64(),
            Some(0.0)
        );
        let output: Value = serde_json::from_slice(&std::fs::read(file).unwrap()).unwrap();
        assert_eq!(output["music"]["title"], "External");
        assert_eq!(output["custom"]["keep"], true);
    });
}

#[test]
fn native_preflight_offline_is_read_only_and_uses_current_configuration() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let before = call(&window, "get_settings", json!({})).unwrap();
        let first = call(&window, "dashboard_preflight", json!({})).unwrap();
        let checks = first["checks"].as_array().unwrap();
        assert_eq!(checks.len(), 9);
        for key in ["obs", "twitch", "music", "title", "category"] {
            assert_eq!(
                checks.iter().find(|c| c["key"] == key).unwrap()["ok"],
                false
            );
        }
        assert!(first["warningCount"].as_u64().unwrap() >= 5);
        assert!(!first.to_string().contains("Streamer.bot"));
        assert_eq!(call(&window, "get_settings", json!({})).unwrap(), before);
        let mut changed = before.clone();
        changed["Obs"]["StartScene"] = json!("New Intro");
        app.state::<AppState>()
            .settings
            .save_edit(&before, &changed)
            .await
            .unwrap();
        let second = call(&window, "dashboard_preflight", json!({})).unwrap();
        assert_eq!(
            second["checks"]
                .as_array()
                .unwrap()
                .iter()
                .find(|c| c["key"] == "start_scene")
                .unwrap()["detail"],
            "New Intro"
        );
        assert!(call(&window, "stream_history_snapshot", json!({})).unwrap()["active"].is_null());
    });
}

#[test]
fn goal_commands_preserve_parallel_settings_and_update_http_layouts_and_websocket() {
    use futures_util::StreamExt;
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let store = OverlayLayoutStore::with_hub(&state.paths.overlay_layouts, state.hub.clone());
        store.save("one",&json!({"items":[{"id":"goal","type":"goal-bar","props":{"kind":"followers","current":999}}]})).await.unwrap();
        let original = call(&window, "twitch_goals_snapshot", json!({})).unwrap();
        let mut draft = original["draft"].clone();
        draft["follower"]["target"] = json!("250,5");
        draft["follower"]["title"] = json!(" Community ");
        let mut concurrent = state.settings.load().await.unwrap();
        concurrent.branding.display_name = "Parallel".into();
        state.settings.save(&concurrent).await.unwrap();
        let server = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            state.hub.clone(),
            0,
        )
        .await
        .unwrap();
        let (mut ws, _) =
            tokio_tungstenite::connect_async(format!("ws://127.0.0.1:{}/ws", server.port))
                .await
                .unwrap();
        ws.next().await.unwrap().unwrap();
        ws.next().await.unwrap().unwrap();
        let saved = call(
            &window,
            "save_twitch_goals",
            json!({"draft":draft,"original":original["original"]}),
        )
        .unwrap();
        assert_eq!(saved["original"]["Branding"]["DisplayName"], "Parallel");
        assert_eq!(saved["original"]["Twitch"]["FollowerGoal"]["Target"], 250.5);
        let event = tokio::time::timeout(std::time::Duration::from_secs(2), ws.next())
            .await
            .unwrap()
            .unwrap()
            .unwrap();
        let event: Value = serde_json::from_str(event.to_text().unwrap()).unwrap();
        assert_eq!(event["type"], "app.overlay.layout");
        let http = reqwest::Client::new();
        let live: Value = http
            .get(format!(
                "http://127.0.0.1:{}/data/overlay-data.json",
                server.port
            ))
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(live["twitch"]["followerGoalState"]["target"], 250.5);
        assert_eq!(live["twitch"]["followerGoalState"]["title"], "Community");
        assert_eq!(
            store.load("one").await.unwrap()["items"][0]["props"]["target"],
            250.5
        );
        let conflict = call(
            &window,
            "save_twitch_goals",
            json!({"draft":original["draft"],"original":original["original"]}),
        );
        assert!(conflict.is_err());
        assert_eq!(
            state.settings.read_value().await.unwrap()["Twitch"]["FollowerGoal"]["Target"],
            250.5
        );
        ws.close(None).await.unwrap();
        server.stop();
        drop(app);
        let restart = test_app(root.path().into());
        let second = WebviewWindowBuilder::new(&restart, "main", Default::default())
            .build()
            .unwrap();
        assert_eq!(
            call(&second, "twitch_goals_snapshot", json!({})).unwrap()["draft"]["follower"]
                ["target"],
            "250.5"
        );
    });
}

#[test]
fn goal_save_reports_followup_failures_and_can_retry_without_losing_saved_settings() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app(root.path().into());
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let settings = state.settings.load().await.unwrap();
        let file = ccs_core::paths::overlay_data_path(&state.paths, &settings);
        std::fs::create_dir_all(&file).unwrap();
        std::fs::create_dir_all(&state.paths.overlay_layouts).unwrap();
        let layout = state.paths.overlay_layouts.join("broken.json");
        std::fs::write(&layout, b"{broken").unwrap();
        let initial = call(&window, "twitch_goals_snapshot", json!({})).unwrap();
        let mut draft = initial["draft"].clone();
        draft["donation"]["reason"] = json!("Mikrofon");
        let saved = call(
            &window,
            "save_twitch_goals",
            json!({"original":initial["original"],"draft":draft}),
        )
        .unwrap();
        assert_eq!(saved["warnings"].as_array().unwrap().len(), 2);
        assert_eq!(
            state.settings.read_value().await.unwrap()["Twitch"]["DonationGoal"]["Reason"],
            "Mikrofon"
        );
        assert_eq!(std::fs::read(&layout).unwrap(), b"{broken");
        std::fs::remove_dir(&file).unwrap();
        std::fs::write(
            &layout,
            json!({"items":[{"type":"goal-bar","props":{"kind":"custom"}}]}).to_string(),
        )
        .unwrap();
        let retried = call(
            &window,
            "save_twitch_goals",
            json!({"original":saved["original"],"draft":saved["draft"]}),
        )
        .unwrap();
        assert_eq!(retried["warnings"], json!([]));
        assert_eq!(
            serde_json::from_slice::<Value>(&std::fs::read(layout).unwrap()).unwrap()["items"][0]
                ["props"]["label"],
            "Donation-Ziel · Mikrofon"
        );
        assert!(file.is_file());
    });
}

#[test]
fn moderation_commands_cross_native_ipc_http_events_and_preserve_the_log() {
    use ccs_modules::twitch::{TwitchOAuthClient, TwitchTokenRepository, TwitchTokenSet};
    use ccs_secrets::MemorySecretStore;
    use wiremock::{
        matchers::{method, path, query_param},
        Mock, MockServer, ResponseTemplate,
    };
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let server = MockServer::start().await;
        let client_id = "abcdefghijabcdefghijabcdefghij";
        Mock::given(path("/validate"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({"client_id":client_id,"login":"owner","user_id":"10","scopes":[],"expires_in":3600})))
            .mount(&server).await;
        Mock::given(path("/users"))
            .respond_with(ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"10","login":"owner","display_name":"Owner"}]}),
            ))
            .mount(&server)
            .await;
        Mock::given(method("DELETE"))
            .and(path("/moderation/chat"))
            .and(query_param("message_id", "m"))
            .respond_with(ResponseTemplate::new(204))
            .expect(1)
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
                client_id: client_id.into(),
                channel_name: String::new(),
                scopes: vec![],
                enable_event_sub: false,
            })
            .await
            .unwrap();
        let app = test_app_with_clients(root.path().into(), None, Some(twitch));
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let mut settings = state.settings.load().await.unwrap();
        settings.twitch.client_id = client_id.into();
        state.settings.save(&settings).await.unwrap();
        state.bridge.from_twitch(
            "channel.chat.message",
            "Hallo",
            "2026-10-03T12:00:00Z".parse().unwrap(),
            std::collections::BTreeMap::from([
                ("messageId".into(), "m".into()),
                ("userId".into(), "42".into()),
            ]),
        );
        let notifications = Arc::new(std::sync::Mutex::new(Vec::new()));
        let received = notifications.clone();
        let listener = app.listen("twitch-event", move |event| {
            received
                .lock()
                .unwrap()
                .push(serde_json::from_str::<Value>(event.payload()).unwrap());
        });
        let changed = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let counter = changed.clone();
        let changed_listener = app.listen("twitch-moderation-changed", move |_| {
            counter.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        });
        let result = call(
            &window,
            "twitch_moderate",
            json!({"action":{"action":"delete_message","messageId":" m "}}),
        )
        .unwrap();
        assert_eq!(result["applied"], true);
        assert!(result.get("event").is_none());
        assert_eq!(
            call(&window, "twitch_chat_feed", json!({})).unwrap()["events"],
            json!([])
        );
        assert_eq!(state.hub.history()["events"], json!([]));
        assert_eq!(notifications.lock().unwrap()[0]["source"], "app");
        assert_eq!(notifications.lock().unwrap()[0]["data"]["message_id"], "m");
        assert_eq!(changed.load(std::sync::atomic::Ordering::SeqCst), 1);
        let snapshot = call(&window, "twitch_moderation_snapshot", json!({})).unwrap();
        assert!(snapshot["entries"][0].as_str().unwrap().contains("LÖSCHEN"));
        let file = root.path().join("Logs/twitch-moderation.log");
        let bytes = std::fs::read(&file).unwrap();
        let export = root.path().join("export.txt");
        call(
            &window,
            "export_twitch_moderation_log",
            json!({"path":export}),
        )
        .unwrap();
        assert_eq!(std::fs::read(export).unwrap(), bytes);
        call(&window, "clear_twitch_moderation_view", json!({})).unwrap();
        assert_eq!(
            call(&window, "twitch_moderation_snapshot", json!({})).unwrap()["entries"],
            json!([])
        );
        assert_eq!(std::fs::read(&file).unwrap(), bytes);
        assert!(call(
            &window,
            "twitch_moderate",
            json!({"action":{"action":"delete_message","messageId":" "}})
        )
        .is_err());
        assert_eq!(notifications.lock().unwrap().len(), 1);
        app.unlisten(listener);
        app.unlisten(changed_listener);
        drop(app);
        let restart = test_app(root.path().into());
        let second = WebviewWindowBuilder::new(&restart, "main", Default::default())
            .build()
            .unwrap();
        assert_eq!(
            call(&second, "twitch_moderation_snapshot", json!({})).unwrap()["entries"],
            json!([])
        );
        assert_eq!(std::fs::read(file).unwrap(), bytes);
    });
}

#[test]
fn native_app_chat_has_500_entries_independent_of_overlay_and_keeps_moderation_in_sync() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let state = app.state::<AppState>();
    for index in 0..600 {
        state.bridge.from_twitch(
            "channel.chat.message",
            "Hallo",
            "2026-10-03T12:00:00Z".parse().unwrap(),
            std::collections::BTreeMap::from([
                ("messageId".into(), index.to_string()),
                ("userId".into(), "user".into()),
                (
                    "parts".into(),
                    "[{\"type\":\"text\",\"text\":\"Hallo\"}]".into(),
                ),
            ]),
        );
    }
    let chat = call(&window, "twitch_chat_feed", json!({})).unwrap();
    assert_eq!(chat["events"].as_array().unwrap().len(), 500);
    assert_eq!(chat["events"][0]["data"]["messageId"], "100");
    assert_eq!(
        call(&window, "chat_history", json!({})).unwrap()["events"]
            .as_array()
            .unwrap()
            .len(),
        160
    );
    assert_eq!(
        call(&window, "twitch_event_feed", json!({})).unwrap()["events"],
        json!([])
    );
    state.bridge.from_twitch(
        "channel.chat.message_delete",
        "Gelöscht",
        "2026-10-03T12:00:01Z".parse().unwrap(),
        std::collections::BTreeMap::from([("message_id".into(), "599".into())]),
    );
    assert_eq!(
        call(&window, "twitch_chat_feed", json!({})).unwrap()["events"]
            .as_array()
            .unwrap()
            .len(),
        499
    );
    assert_eq!(state.hub.history()["events"].as_array().unwrap().len(), 159);
    tauri::async_runtime::block_on(async {
        let mut settings = state.settings.load().await.unwrap();
        settings.twitch.enable_chat = false;
        state.settings.save(&settings).await.unwrap();
        assert_eq!(
            call(&window, "twitch_chat_feed", json!({})).unwrap()["events"],
            json!([])
        );
        settings.twitch.enable_chat = true;
        state.settings.save(&settings).await.unwrap();
        assert_eq!(
            call(&window, "twitch_chat_feed", json!({})).unwrap()["events"]
                .as_array()
                .unwrap()
                .len(),
            499
        );
    });
    state.bridge.from_twitch(
        "channel.chat.clear_user_messages",
        "Timeout",
        "2026-10-03T12:00:02Z".parse().unwrap(),
        std::collections::BTreeMap::from([("target_user_id".into(), "user".into())]),
    );
    assert_eq!(
        call(&window, "twitch_chat_feed", json!({})).unwrap()["events"],
        json!([])
    );
    assert_eq!(state.hub.history()["events"], json!([]));
}

#[test]
fn native_twitch_feed_reads_the_shared_bridge_after_page_changes_even_when_chat_is_disabled() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    tauri::async_runtime::block_on(async {
        let state = app.state::<AppState>();
        let server = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            state.hub.clone(),
            0,
        )
        .await
        .unwrap();
        let (mut ws, _) =
            tokio_tungstenite::connect_async(format!("ws://127.0.0.1:{}/ws", server.port))
                .await
                .unwrap();
        use futures_util::StreamExt;
        for _ in 0..2 {
            tokio::time::timeout(std::time::Duration::from_secs(3), ws.next())
                .await
                .unwrap()
                .unwrap()
                .unwrap();
        }
        let expected = state.bridge.from_twitch(
            "channel.follow",
            "Alice folgt dem Kanal.",
            "2026-10-03T12:00:00Z".parse().unwrap(),
            std::collections::BTreeMap::from([("user_name".into(), "Alice".into())]),
        );
        let frame = tokio::time::timeout(std::time::Duration::from_secs(3), ws.next())
            .await
            .unwrap()
            .unwrap()
            .unwrap();
        assert_eq!(
            serde_json::from_str::<Value>(frame.to_text().unwrap()).unwrap(),
            expected
        );
        state.bridge.from_twitch(
            "channel.chat.message",
            "Chat",
            "2026-10-03T12:00:01Z".parse().unwrap(),
            std::collections::BTreeMap::new(),
        );
        let mut settings = state.settings.load().await.unwrap();
        settings.twitch.enable_chat = false;
        state.settings.save(&settings).await.unwrap();
        assert_eq!(
            call(&window, "chat_history", json!({})).unwrap()["events"],
            json!([])
        );
        assert_eq!(
            call(&window, "twitch_event_feed", json!({})).unwrap()["events"],
            json!([expected.clone()])
        );
        let second = WebviewWindowBuilder::new(&app, "other-page", Default::default())
            .build()
            .unwrap();
        assert_eq!(
            call(&second, "twitch_event_feed", json!({})).unwrap()["events"],
            json!([expected])
        );
        server.stop();
    });
}

#[test]
fn native_canvas_deletion_updates_the_same_chat_capacity_and_history_as_http() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    tauri::async_runtime::block_on(async {
        let state = app.state::<AppState>();
        let layouts = OverlayLayoutStore::new(&state.paths.overlay_layouts);
        layouts
            .save(
                "default",
                &json!({"items":[{"type":"chat","props":{"maxLines":500}}]}),
            )
            .await
            .unwrap();
        let server = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            state.hub.clone(),
            0,
        )
        .await
        .unwrap();
        assert_eq!(state.hub.chat_capacity(), 1000);
        let copy = call(&window, "duplicate_canvas", json!({"id":"default"})).unwrap();
        let id = copy["id"].as_str().unwrap();
        let http = reqwest::Client::new();
        http.put(format!("http://127.0.0.1:{}/layout/{id}", server.port))
            .json(&json!({"items":[{"type":"chat","props":{"maxLines":120}}]}))
            .send()
            .await
            .unwrap()
            .error_for_status()
            .unwrap();
        for index in 0..900 {
            state.hub.publish(&json!({"source":"twitch","type":"channel.chat.message","data":{"messageId":index.to_string()}}));
        }
        call(&window, "delete_canvas", json!({"id":"default"})).unwrap();
        assert_eq!(state.hub.chat_capacity(), 240);
        assert_eq!(
            call(&window, "chat_history", json!({})).unwrap()["events"]
                .as_array()
                .unwrap()
                .len(),
            240
        );
        assert_eq!(state.hub.history()["events"][0]["data"]["messageId"], "660");
        state.hub.flush_history().unwrap();
        let fresh = Arc::new(RealtimeHub::new());
        let second = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            fresh.clone(),
            0,
        )
        .await
        .unwrap();
        assert_eq!(fresh.chat_capacity(), 240);
        assert_eq!(fresh.history(), state.hub.history());
        second.stop();
        server.stop();
    });
}

#[test]
fn native_history_root_change_reports_failure_without_overwriting_the_corrupt_target() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    tauri::async_runtime::block_on(async {
        let state = app.state::<AppState>();
        let server = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            state.hub.clone(),
            0,
        )
        .await
        .unwrap();
        state.hub.publish(&json!({"source":"twitch","type":"channel.chat.message","data":{"messageId":"retained"}}));
        let target = root.path().join("other");
        std::fs::create_dir_all(&target).unwrap();
        let file = target.join("chat-history.json");
        std::fs::write(&file, "corrupt").unwrap();
        let original = call(&window, "get_settings", json!({})).unwrap();
        let mut edited = original.clone();
        edited["Overlay"]["RootPath"] = json!(target.to_string_lossy());
        let result = call(
            &window,
            "save_settings",
            json!({"original":original,"settings":edited}),
        )
        .unwrap();
        assert_eq!(result["saved"], true);
        assert!(result["warnings"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e.as_str().unwrap().contains("Chat-Verlauf")));
        assert_eq!(std::fs::read_to_string(&file).unwrap(), "corrupt");
        assert_eq!(
            state.hub.history()["events"][0]["data"]["messageId"],
            "retained"
        );
        let status = call(&window, "overlay_runtime_status", json!({})).unwrap();
        assert!(status["error"].as_str().unwrap().contains("Chat-Verlauf"));
        std::fs::write(&file, "[]").unwrap();
        let saved = state.settings.load().await.unwrap();
        state
            .hub
            .configure_history(ccs_overlay_server::chat_history_path(&state.paths, &saved))
            .unwrap();
        assert!(call(&window, "overlay_runtime_status", json!({})).unwrap()["error"].is_null());
        assert!(state.hub.history()["events"].as_array().unwrap().is_empty());
        let previous: Value = serde_json::from_slice(
            &std::fs::read(state.paths.overlay_root.join("chat-history.json")).unwrap(),
        )
        .unwrap();
        assert_eq!(previous[0]["data"]["messageId"], "retained");
        server.stop();
    });
}

#[test]
fn saved_chat_appearance_notifies_actual_websocket_clients_and_survives_restart() {
    use futures_util::StreamExt;
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    tauri::async_runtime::block_on(async {
        let state = app.state::<AppState>();
        let server = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            state.hub.clone(),
            0,
        )
        .await
        .unwrap();
        let (mut ws, _) =
            tokio_tungstenite::connect_async(format!("ws://127.0.0.1:{}/ws", server.port))
                .await
                .unwrap();
        ws.next().await.unwrap().unwrap(); // hello
        ws.next().await.unwrap().unwrap(); // current countdown snapshot
        let original = call(&window, "get_settings", json!({})).unwrap();
        let mut edited = original.clone();
        edited["Overlay"]["Chat"]["FontSizePx"] = json!(28);
        edited["Overlay"]["Chat"]["BackgroundType"] = json!("Color");
        edited["Overlay"]["Chat"]["BackgroundColor"] = json!("#123456");
        call(
            &window,
            "save_settings",
            json!({"original": original,"settings": edited}),
        )
        .unwrap();
        let event = tokio::time::timeout(std::time::Duration::from_secs(2), ws.next())
            .await
            .expect("saved config must notify connected overlays")
            .unwrap()
            .unwrap();
        let event: Value = serde_json::from_str(event.to_text().unwrap()).unwrap();
        assert_eq!(event["type"], "app.chat.config");
        assert_eq!(event["source"], "app");
        let config: Value = reqwest::get(format!("http://127.0.0.1:{}/chat/config", server.port))
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(config["fontSizePx"], 28);
        assert_eq!(config["backgroundColor"], "#123456");
        call(
            &window,
            "save_settings",
            json!({"original": edited, "settings": edited}),
        )
        .unwrap();
        assert!(
            tokio::time::timeout(std::time::Duration::from_millis(80), ws.next())
                .await
                .is_err()
        );
        let mut invalid = edited.clone();
        invalid["Overlay"]["Chat"]["FontSizePx"] = json!(30);
        invalid["Obs"]["Host"] = json!("");
        assert!(call(
            &window,
            "save_settings",
            json!({"original": edited,"settings": invalid})
        )
        .is_err());
        assert!(
            tokio::time::timeout(std::time::Duration::from_millis(80), ws.next())
                .await
                .is_err()
        );
        let reloaded = JsonSettingsStore::new(&state.paths.settings_file)
            .load()
            .await
            .unwrap();
        assert_eq!(reloaded.overlay.chat.extra["FontSizePx"], 28);
        server.stop();
    });
}

#[test]
fn chat_catalog_status_and_refresh_cross_native_ipc_without_faking_a_connection() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let initial = call(&window, "chat_catalog_status", json!({})).unwrap();
    assert_eq!(initial["emotes"], 0);
    assert_eq!(initial["badges"], 0);
    assert!(initial["updatedAt"].is_null());
    let refreshed = call(&window, "refresh_chat_catalogs", json!({})).unwrap();
    assert_eq!(refreshed["emotes"], 0);
    assert!(!refreshed["errors"].as_array().unwrap().is_empty());
    assert_eq!(
        call(&window, "chat_catalog_status", json!({})).unwrap(),
        refreshed
    );
}

#[test]
fn extension_commands_use_the_same_persistent_library_as_canvas_http() {
    use std::io::{Cursor, Write};
    use tauri::Listener;
    let root = tempfile::tempdir().unwrap();
    let file = root.path().join("test.zip");
    let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
    zip.start_file("manifest.json", zip::write::SimpleFileOptions::default())
        .unwrap();
    zip.write_all(
        json!({"id":"native-pack","name":"Native Pack","version":"1.0","apiVersion":1})
            .to_string()
            .as_bytes(),
    )
    .unwrap();
    std::fs::write(&file, zip.finish().unwrap().into_inner()).unwrap();
    let app = test_app(root.path().into());
    let events = Arc::new(std::sync::Mutex::new(Vec::<Value>::new()));
    let recorded = events.clone();
    let listener = app.listen("extension-packs-changed", move |event| {
        recorded
            .lock()
            .unwrap()
            .push(serde_json::from_str(event.payload()).unwrap())
    });
    let forwarding =
        spawn_extension_pack_events(app.handle().clone(), app.state::<AppState>().hub.clone());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    assert_eq!(
        call(&window, "list_extension_packs", json!({})).unwrap(),
        json!([])
    );
    let installed = call(
        &window,
        "import_extension_pack",
        json!({"path":file.to_str().unwrap()}),
    )
    .unwrap();
    assert_eq!(installed["id"], "native-pack");
    assert_eq!(
        call(&window, "list_extension_packs", json!({})).unwrap()[0]["version"],
        "1.0"
    );
    tauri::async_runtime::block_on(async {
        let state = app.state::<AppState>();
        let server = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            state.hub.clone(),
            0,
        )
        .await
        .unwrap();
        let base = format!("http://127.0.0.1:{}", server.port);
        let data: Value = reqwest::get(format!("{base}/extensions"))
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(data["packs"][0]["id"], "native-pack");
        std::fs::write(&file, b"invalid zip").unwrap();
        assert!(call(
            &window,
            "import_extension_pack",
            json!({"path":file.to_str().unwrap()})
        )
        .is_err());
        assert!(call(
            &window,
            "import_extension_pack",
            json!({"path":root.path().to_str().unwrap()})
        )
        .is_err());
        assert_eq!(
            call(&window, "list_extension_packs", json!({})).unwrap()[0]["version"],
            "1.0"
        );
        call(
            &window,
            "uninstall_extension_pack",
            json!({"id":"native-pack"}),
        )
        .unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(2), async {
            while events.lock().unwrap().len() < 2 {
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
        let changes = events.lock().unwrap().clone();
        assert_eq!(changes,json!([{"action":"installed","packId":"native-pack"},{"action":"uninstalled","packId":"native-pack"}]).as_array().unwrap().clone());
        assert_eq!(
            reqwest::get(format!("{base}/ext/native-pack/manifest.json"))
                .await
                .unwrap()
                .status(),
            404
        );
        server.stop();
    });
    forwarding.abort();
    app.unlisten(listener);
}

#[test]
fn spotify_commands_update_shared_state_and_overlay_through_native_ipc() {
    use ccs_modules::spotify::{SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet};
    use ccs_secrets::MemorySecretStore;
    use wiremock::{
        matchers::{method, path, query_param},
        Mock, MockServer, ResponseTemplate,
    };
    let root = tempfile::tempdir().unwrap();
    let (spotify, server) = tauri::async_runtime::block_on(async {
        let server = MockServer::start().await;
        let secrets = Arc::new(MemorySecretStore::new());
        SpotifyTokenRepository::new(secrets.clone())
            .save(&SpotifyTokenSet::from_oauth(
                "saved-access".into(),
                "saved-refresh".into(),
                3600,
                "Bearer".into(),
                vec![],
            ))
            .unwrap();
        Mock::given(method("GET"))
            .and(path("/me"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({"id":"user"})))
            .mount(&server)
            .await;
        Mock::given(method("GET")).and(path("/me/player/currently-playing"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({"is_playing":true,
                "progress_ms":500,"item":{"id":"track","name":"Live contract","duration_ms":4000}})))
            .mount(&server).await;
        for endpoint in ["pause", "play"] {
            Mock::given(method("PUT"))
                .and(path(format!("/me/player/{endpoint}")))
                .respond_with(ResponseTemplate::new(204))
                .expect(1)
                .mount(&server)
                .await;
        }
        Mock::given(method("PUT"))
            .and(path("/me/player/seek"))
            .and(query_param("position_ms", "4000"))
            .respond_with(ResponseTemplate::new(204))
            .expect(1)
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
    let app = test_app_with_spotify(root.path().into(), Some(spotify));
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut next = original.clone();
    next["Spotify"]["ClientId"] = json!("contract-client-id-12345");
    next["Spotify"]["OverlayHideWhenMuted"] = json!(false);
    call(
        &window,
        "save_settings",
        json!({"original":original,"settings":next}),
    )
    .unwrap();
    call(&window, "music_player_connect", json!({})).unwrap();
    call(
        &window,
        "music_player_action",
        json!({"action":{"action":"pause"}}),
    )
    .unwrap();
    assert_eq!(
        call(&window, "music_player_snapshot", json!({})).unwrap()["isPlaying"],
        false
    );
    // The legacy command must use the same cache as the shared player.
    call(
        &window,
        "spotify_action",
        json!({"action":{"action":"play"}}),
    )
    .unwrap();
    assert_eq!(
        call(&window, "music_overlay_snapshot", json!({})).unwrap()["isPlaying"],
        true
    );
    call(
        &window,
        "music_player_action",
        json!({"action":{"action":"seek","positionMs":9000}}),
    )
    .unwrap();
    assert_eq!(
        call(&window, "music_player_snapshot", json!({})).unwrap()["progressMs"],
        4000
    );
    tauri::async_runtime::block_on(async {
        let state = app.state::<AppState>();
        let settings = state.settings.load().await.unwrap();
        let snapshot = state.music_player.snapshot().await.unwrap();
        runtime::update_music_data(&state, &snapshot, &settings);
        let file = root.path().join("data.json");
        state.hub.live.write_snapshot(&file).await.unwrap();
        let overlay = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            state.hub.clone(),
            0,
        )
        .await
        .unwrap();
        let data: Value = reqwest::get(format!(
            "http://127.0.0.1:{}/data/overlay-data.json",
            overlay.port
        ))
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
        assert_eq!(data["music"]["isPlaying"], true);
        assert_eq!(data["music"]["progressMs"], 4000);
        assert_eq!(data["music"], data["spotify"]);
        let disk: Value = serde_json::from_slice(&tokio::fs::read(file).await.unwrap()).unwrap();
        assert_eq!(disk["music"], data["music"]);
        overlay.stop();
    });
    call(&window, "music_player_disconnect", json!({})).unwrap();
    tauri::async_runtime::block_on(server.verify());
}

#[test]
fn music_overlay_settings_and_snapshot_cross_ipc_file_and_actual_http() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let socket = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = socket.local_addr().unwrap().port();
    drop(socket);
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut next = original.clone();
    next["MusicPlayer"]["ProviderId"] = json!("ytmusic");
    next["MusicPlayer"]["ShowTitle"] = json!(false);
    next["Spotify"]["OverlayHideWhenMuted"] = json!(false);
    next["Spotify"]["OverlayObsAudioSource"] = json!("YouTube");
    next["YouTubeMusic"]["BridgePort"] = json!(port);
    call(
        &window,
        "save_settings",
        json!({"original":original,"settings":next}),
    )
    .unwrap();
    call(&window, "ytm_connect", json!({})).unwrap();
    tauri::async_runtime::block_on(async {
        let http = reqwest::Client::new();
        http.post(format!("http://127.0.0.1:{port}/ytmusic/state"))
            .json(&json!({"title":"Shared","artist":"Artist","isPlaying":true,"coverUrl":"Cover"}))
            .send()
            .await
            .unwrap()
            .error_for_status()
            .unwrap();
        let payload = call(&window, "music_overlay_snapshot", json!({})).unwrap();
        assert_eq!(payload["hideWhenMuted"], false);
        assert_eq!(payload["showTitle"], true);
        assert_eq!(payload["obsAudioSource"], "YouTube");
        assert_eq!(payload["visible"], true);
        let state = app.state::<AppState>();
        let settings = state.settings.load().await.unwrap();
        let snapshot = state.music_player.snapshot().await.unwrap();
        runtime::update_music_data(&state, &snapshot, &settings);
        let file = root.path().join("data.json");
        state
            .hub
            .live
            .write_snapshot_with_music(&file, true)
            .await
            .unwrap();
        let server = OverlayServer::start(
            state.settings.clone(),
            state.paths.clone(),
            state.hub.clone(),
            0,
        )
        .await
        .unwrap();
        let data: Value = http
            .get(format!(
                "http://127.0.0.1:{}/data/overlay-data.json",
                server.port
            ))
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(data["music"]["title"], "Shared");
        assert_eq!(data["spotify"]["cover"], "Cover");
        assert_eq!(data["music"], data["spotify"]);
        let disk: Value = serde_json::from_slice(&tokio::fs::read(&file).await.unwrap()).unwrap();
        assert_eq!(disk["music"], data["music"]);
        let mut disabled = next.clone();
        disabled["Spotify"]["OverlayEnabled"] = json!(false);
        call(
            &window,
            "save_settings",
            json!({"original":next,"settings":disabled}),
        )
        .unwrap();
        tokio::fs::write(
            &file,
            json!({"music":{"title":"External"},"spotify":{"title":"External"}}).to_string(),
        )
        .await
        .unwrap();
        let settings = state.settings.load().await.unwrap();
        runtime::update_music_data(&state, &snapshot, &settings);
        state
            .hub
            .live
            .write_snapshot_with_music(&file, false)
            .await
            .unwrap();
        let data: Value = http
            .get(format!(
                "http://127.0.0.1:{}/data/overlay-data.json",
                server.port
            ))
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(data["music"]["title"], "External");
        assert_eq!(
            call(&window, "music_player_snapshot", json!({})).unwrap()["title"],
            "Shared"
        );
        server.stop();
    });
    call(&window, "music_player_disconnect", json!({})).unwrap();
}

#[test]
fn shared_player_commands_cross_native_ipc_and_provider_switch_releases_the_bridge() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let snapshot = call(&window, "music_player_snapshot", json!({})).unwrap();
    assert_eq!(snapshot["provider"], "spotify");
    assert_eq!(snapshot["connected"], false);
    assert_eq!(snapshot["volumePercent"], Value::Null);
    assert!(call(
        &window,
        "music_player_action",
        json!({"action":{"action":"play"}})
    )
    .is_err());
    assert!(
        call(&window, "ytm_connect", json!({})).is_err(),
        "inactive providers cannot connect"
    );
    let socket = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = socket.local_addr().unwrap().port();
    drop(socket);
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut next = original.clone();
    next["MusicPlayer"]["ProviderId"] = json!("ytmusic");
    next["YouTubeMusic"]["BridgePort"] = json!(port);
    call(
        &window,
        "save_settings",
        json!({"original":original,"settings":next}),
    )
    .unwrap();
    let connected = call(&window, "music_player_connect", json!({})).unwrap();
    assert!(connected["installUrl"]
        .as_str()
        .unwrap()
        .contains(&port.to_string()));
    tauri::async_runtime::block_on(async {
        let http = reqwest::Client::new();
        http.post(format!("http://127.0.0.1:{port}/ytmusic/state")).json(&json!({"title":"Shared title","coverUrl":"https://example.com/image","isPlaying":true})).send().await.unwrap().error_for_status().unwrap();
        let snapshot = call(&window, "music_player_snapshot", json!({})).unwrap();
        assert_eq!(snapshot["title"], "Shared title");
        assert_eq!(snapshot["supportsVolume"], false);
        call(
            &window,
            "music_player_action",
            json!({"action":{"action":"play_pause"}}),
        )
        .unwrap();
        let commands: Value = http
            .get(format!("http://127.0.0.1:{port}/ytmusic/commands"))
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(commands["commands"], json!(["playpause"]));
    });
    let mut spotify = next.clone();
    spotify["MusicPlayer"]["ProviderId"] = json!("spotify");
    call(
        &window,
        "save_settings",
        json!({"original":next,"settings":spotify}),
    )
    .unwrap();
    assert_eq!(
        call(&window, "ytm_runtime_status", json!({})).unwrap()["running"],
        false
    );
    std::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, port)).unwrap();
    call(&window, "music_player_disconnect", json!({})).unwrap();
}

#[test]
fn common_connect_reuses_saved_spotify_oauth_after_disconnect_through_native_ipc() {
    use ccs_modules::spotify::{SpotifyOAuthClient, SpotifyTokenRepository, SpotifyTokenSet};
    use ccs_secrets::MemorySecretStore;
    use wiremock::{
        matchers::{method, path},
        Mock, MockServer, ResponseTemplate,
    };
    let root = tempfile::tempdir().unwrap();
    let (spotify, _server) = tauri::async_runtime::block_on(async {
        let server = MockServer::start().await;
        let secrets = Arc::new(MemorySecretStore::new());
        SpotifyTokenRepository::new(secrets.clone())
            .save(&SpotifyTokenSet::from_oauth(
                "saved-access".into(),
                "saved-refresh".into(),
                3600,
                "Bearer".into(),
                vec![],
            ))
            .unwrap();
        Mock::given(method("GET"))
            .and(path("/me"))
            .respond_with(
                ResponseTemplate::new(200)
                    .set_body_json(json!({"id":"user","display_name":"Saved User"})),
            )
            .expect(2)
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/me/player/currently-playing"))
            .respond_with(ResponseTemplate::new(204))
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
    let app = test_app_with_spotify(root.path().into(), Some(spotify.clone()));
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut next = original.clone();
    next["Spotify"]["ClientId"] = json!("contract-client-id-12345");
    call(
        &window,
        "save_settings",
        json!({"original":original,"settings":next}),
    )
    .unwrap();
    assert_eq!(
        call(&window, "music_player_connect", json!({})).unwrap()["state"],
        "connected"
    );
    call(&window, "music_player_disconnect", json!({})).unwrap();
    assert!(spotify.has_token());
    assert_eq!(
        call(&window, "music_player_snapshot", json!({})).unwrap()["connected"],
        false
    );
    assert_eq!(
        call(&window, "music_player_connect", json!({})).unwrap()["state"],
        "connected"
    );
    call(&window, "music_player_disconnect", json!({})).unwrap();
}

#[test]
fn youtube_autostart_respects_selected_provider_and_reports_busy_port_without_aborting_app() {
    tauri::async_runtime::block_on(async {
        let mut settings = AppSettings::default();
        assert!(initial_ytm(&settings).await.0.is_none());
        settings.music_player.source = "ytmusic".into();
        settings.you_tube_music.extra = json!({"AutoConnect":false});
        assert!(initial_ytm(&settings).await.0.is_none());
        let blocked = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = blocked.local_addr().unwrap().port();
        settings.you_tube_music.extra =
            json!({"BridgePort":port,"AutoConnect":true,"StateTimeoutSeconds":30});
        let (bridge, error) = initial_ytm(&settings).await;
        assert!(bridge.is_none());
        assert!(error.unwrap().contains(&port.to_string()));
        drop(blocked);
        let (bridge, error) = initial_ytm(&settings).await;
        assert!(error.is_none());
        let bridge = bridge.unwrap();
        assert!(bridge.is_running());
        bridge.stop_and_wait().await;
    });
}

#[test]
fn youtube_bridge_port_changes_are_transactional_and_setup_crosses_native_ipc() {
    let root = tempfile::tempdir().unwrap();
    let app = test_app(root.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let first = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let first_port = first.local_addr().unwrap().port();
    drop(first);
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut next = original.clone();
    next["MusicPlayer"]["ProviderId"] = json!("ytmusic");
    next["YouTubeMusic"] =
        json!({"BridgePort":first_port,"StateTimeoutSeconds":30,"AutoConnect":true,"Custom":42});
    call(
        &window,
        "save_settings",
        json!({"original":original,"settings":next}),
    )
    .unwrap();
    let install = call(&window, "ytm_connect", json!({})).unwrap();
    assert!(install.as_str().unwrap().contains(&first_port.to_string()));
    let status = call(&window, "ytm_runtime_status", json!({})).unwrap();
    assert_eq!(status["running"], true);
    assert_eq!(status["port"], first_port);
    assert!(status["bookmarklet"]
        .as_str()
        .unwrap()
        .starts_with("javascript:%"));
    tauri::async_runtime::block_on(async {
        let client = reqwest::Client::new();
        let base = format!("http://127.0.0.1:{first_port}/ytmusic");
        client.post(format!("{base}/state")).json(&json!({"title":"Song","artist":"Artist","coverUrl":"https://example.com/cover.png","durationMs":5000,"progressMs":2000})).send().await.unwrap().error_for_status().unwrap();
        assert_eq!(
            call(&window, "ytm_now_playing", json!({})).unwrap()["title"],
            "Song"
        );
        call(&window, "ytm_command", json!({"command":"pause"})).unwrap();
        let commands: Value = client
            .get(format!("{base}/commands"))
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        assert_eq!(commands["commands"], json!(["pause"]));
    });
    let blocked = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let blocked_port = blocked.local_addr().unwrap().port();
    let mut invalid = next.clone();
    invalid["YouTubeMusic"]["BridgePort"] = json!(blocked_port);
    assert!(call(
        &window,
        "save_settings",
        json!({"original":next,"settings":invalid})
    )
    .is_err());
    assert_eq!(
        call(&window, "get_settings", json!({})).unwrap()["YouTubeMusic"]["BridgePort"],
        first_port
    );
    assert_eq!(
        call(&window, "ytm_now_playing", json!({})).unwrap()["title"],
        "Song"
    );
    drop(blocked);
    let mut concurrent = next.clone();
    concurrent["YouTubeMusic"]["StateTimeoutSeconds"] = json!(40);
    tauri::async_runtime::block_on(
        app.state::<AppState>()
            .settings
            .save_edit(&next, &concurrent),
    )
    .unwrap();
    let mut conflicting = invalid.clone();
    conflicting["YouTubeMusic"]["StateTimeoutSeconds"] = json!(50);
    assert!(call(
        &window,
        "save_settings",
        json!({"original":next,"settings":conflicting})
    )
    .is_err());
    let candidate =
        std::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, blocked_port)).unwrap();
    drop(candidate);
    assert_eq!(
        call(&window, "ytm_runtime_status", json!({})).unwrap()["port"],
        first_port
    );
    let mut changed = next.clone();
    changed["YouTubeMusic"]["BridgePort"] = json!(blocked_port);
    call(
        &window,
        "save_settings",
        json!({"original":next,"settings":changed}),
    )
    .unwrap();
    assert_eq!(
        call(&window, "ytm_runtime_status", json!({})).unwrap()["port"],
        blocked_port
    );
    assert_eq!(
        call(&window, "get_settings", json!({})).unwrap()["YouTubeMusic"]["Custom"],
        42
    );
    assert_eq!(
        call(&window, "get_settings", json!({})).unwrap()["YouTubeMusic"]["StateTimeoutSeconds"],
        40
    );
    let rebound = std::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, first_port)).unwrap();
    drop(rebound);
    call(&window, "ytm_disconnect", json!({})).unwrap();
    assert_eq!(
        call(&window, "ytm_runtime_status", json!({})).unwrap()["running"],
        false
    );
    let rebound =
        std::net::TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, blocked_port)).unwrap();
    drop(rebound);
}

#[test]
fn statistics_http_samples_emit_native_changes_and_reset_survives_restart() {
    use tauri::Listener;
    use wiremock::{
        matchers::{method, path},
        Mock, MockServer, ResponseTemplate,
    };
    let root = tempfile::tempdir().unwrap();
    let server = tauri::async_runtime::block_on(MockServer::start());
    tauri::async_runtime::block_on(Mock::given(method("GET"))
        .and(path("/me/player/currently-playing"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({"is_playing":true,"item":{"id":"native-song","type":"track","name":"Native Song","artists":[{"name":"Artist"}],"album":{"name":"Album"}}})))
        .mount(&server));
    let secrets = Arc::new(ccs_secrets::MemorySecretStore::new());
    ccs_modules::spotify::SpotifyTokenRepository::new(secrets.clone())
        .save(&ccs_modules::spotify::SpotifyTokenSet {
            access_token: "test-token".into(),
            refresh_token: "refresh".into(),
            obtained_at: std::time::SystemTime::now().into(),
            expires_in_seconds: 3600,
            token_type: "Bearer".into(),
            scopes: vec![],
        })
        .unwrap();
    let player = Arc::new(SpotifyClient::with_http(
        secrets,
        ccs_modules::spotify::SpotifyOAuthClient::new(),
        server.uri(),
    ));
    let app = test_app_with_spotify(root.path().into(), Some(player.clone()));
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let (sent, mut received) = tokio::sync::mpsc::unbounded_channel();
    let listener = app.listen("music-statistics-changed", move |event| {
        sent.send(event.payload().to_string()).unwrap();
    });
    let runtime = app.state::<AppState>().music_statistics.clone();
    let job = spawn_music_statistics(app.handle().clone(), runtime.clone(), &player);
    tauri::async_runtime::block_on(async {
        player.refresh_now_playing("client").await.unwrap();
        let event = tokio::time::timeout(std::time::Duration::from_secs(2), received.recv())
            .await
            .unwrap()
            .unwrap();
        assert_eq!(
            serde_json::from_str::<Value>(&event).unwrap(),
            json!({"changed":true})
        );
    });
    let snapshot = call(&window, "music_statistics_snapshot", json!({})).unwrap();
    assert_eq!(snapshot["totalPlays"], 1);
    assert_eq!(snapshot["topTracks"][0]["TrackId"], "native-song");
    assert_eq!(snapshot["topArtists"][0]["artist"], "Artist");
    call(&window, "reset_music_statistics", json!({})).unwrap();
    tauri::async_runtime::block_on(async {
        runtime.close().await;
        job.await.unwrap();
    });
    app.unlisten(listener);
    let restarted = test_app(root.path().into());
    let second = WebviewWindowBuilder::new(&restarted, "main", Default::default())
        .build()
        .unwrap();
    assert_eq!(
        call(&second, "music_statistics_snapshot", json!({})).unwrap()["totalPlays"],
        0
    );
    let file = root
        .path()
        .join("Statistics/spotify-listening-statistics.json");
    std::fs::write(&file, "corrupt").unwrap();
    assert!(call(&second, "music_statistics_snapshot", json!({})).is_err());
    assert!(call(&second, "reset_music_statistics", json!({})).is_err());
    assert_eq!(std::fs::read_to_string(file).unwrap(), "corrupt");
}

fn test_app(root: PathBuf) -> tauri::App<MockRuntime> {
    test_app_with_spotify(root, None)
}
fn test_app_with_spotify(
    root: PathBuf,
    spotify: Option<Arc<SpotifyClient>>,
) -> tauri::App<MockRuntime> {
    test_app_with_clients(root, spotify, None)
}
pub(super) fn test_app_with_clients(
    root: PathBuf,
    spotify: Option<Arc<SpotifyClient>>,
    twitch: Option<Arc<TwitchClient>>,
) -> tauri::App<MockRuntime> {
    let paths = AppPaths::from_root(root);
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let secrets = Arc::new(KeyringSecretStore::new());
    let hub = Arc::new(RealtimeHub::new());
    let bridge = OverlayEventBridge::new(hub.clone());
    let alerts = Arc::new(AlertEngine::from_store(settings.clone(), bridge.clone()));
    let spotify = spotify.unwrap_or_else(|| SpotifyClient::new_shared(secrets.clone()));
    let ducking = Arc::new(ccs_modules::music_automation::AlertDucking::new(
        spotify.clone(),
    ));
    alerts.attach_music(ducking.clone());
    let scene_music = Arc::new(ccs_modules::scene_music::SceneMusicEngine::new(
        settings.clone(),
        spotify.clone(),
        ducking.clone(),
    ));
    let music_states = Arc::new(ccs_modules::spotify_states::SpotifyStateRuntime::new(
        Arc::new(ccs_core::spotify_states::SpotifyStateStore::new(
            paths.data_root.clone(),
        )),
        settings.clone(),
        spotify.clone(),
        scene_music.clone(),
        ducking.clone(),
    ));
    let music_statistics = Arc::new(ccs_modules::music_statistics::MusicStatisticsRuntime::new(
        Arc::new(ccs_core::music_statistics::MusicStatisticsStore::new(
            &paths.data_root,
        )),
        settings.clone(),
    ));
    let ytm = Arc::new(Mutex::new(None));
    let music_player = Arc::new(ccs_modules::music_player::MusicPlayerRuntime::new(
        settings.clone(),
        spotify.clone(),
        scene_music.clone(),
        ducking,
        ytm.clone(),
    ));
    let obs = ObsClient::new_shared("127.0.0.1", 4455);
    let twitch = twitch.unwrap_or_else(|| TwitchClient::new_shared(secrets.clone()));
    let moderation = Arc::new(ccs_modules::twitch::ModerationRuntime::new(
        settings.clone(),
        twitch.clone(),
        bridge.clone(),
        &paths.logs,
    ));
    let twitch_metrics = Arc::new(ccs_modules::twitch::TwitchMetricsRuntime::new(
        settings.clone(),
        twitch.clone(),
        hub.clone(),
    ));
    let stream_history = Arc::new(ccs_modules::stream_history::StreamHistoryRuntime::new(
        paths.data_root.clone(),
        hub.clone(),
    ));
    bridge.set_stream_history(stream_history.clone());
    let stream_end = Arc::new(ccs_modules::stream_end::StreamEndRuntime::default());
    let notifications = Arc::new(ccs_modules::notifications::NotificationRuntime::new(
        &paths.data_root,
    ));
    runtime::bind_stream_history(
        &obs,
        stream_history.clone(),
        stream_end.clone(),
        notifications.clone(),
    );
    let creator_intelligence = Arc::new(
        ccs_modules::creator_intelligence::CreatorIntelligenceRuntime::new(
            paths.data_root.clone(),
            stream_history.clone(),
        ),
    );
    let music_overlay = Arc::new(ccs_modules::music_overlay::MusicOverlayRuntime::new(
        settings.clone(),
        obs.clone(),
    ));
    mock_builder()
        .manage(StartupState::default())
        .manage(AppState {
            stream_end,
            stream_end_gate: Mutex::new(()),
            ytm,
            music_player,
            music_overlay,
            ytm_error: Mutex::new(None),
            settings_mutation: Mutex::new(()),
            obs,
            twitch,
            moderation,
            twitch_metrics,
            stream_history,
            notifications,
            creator_intelligence,
            spotify,
            scene_music,
            music_states,
            music_statistics,
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
            launch_service,
            stream_end_snapshot,
            stream_end_status,
            save_stream_end_preferences,
            start_stream_end,
            stream_end_control,
            dashboard_snapshot,
            dashboard_preflight,
            save_dashboard,
            dashboard_image_preview,
            dashboard_asset_choices,
            dashboard_obs_preview,
            duplicate_canvas,
            delete_canvas,
            chat_history,
            twitch_event_feed,
            twitch_chat_feed,
            twitch_action,
            twitch_moderate,
            twitch_moderation_snapshot,
            clear_twitch_moderation_view,
            export_twitch_moderation_log,
            twitch_query,
            twitch_metrics_snapshot,
            refresh_twitch_metrics,
            twitch_goals_snapshot,
            stream_history_snapshot,
            notifications_snapshot,
            notifications_mark_read,
            notifications_clear,
            notifications_retry,
            creator_intelligence_snapshot,
            record_creator_note,
            complete_creator_action,
            start_creator_experiment,
            generate_creator_weekly_report,
            retry_stream_history,
            latest_stream_summary,
            export_stream_history,
            save_twitch_goals,
            twitch_raid_settings,
            save_twitch_raid_settings,
            select_twitch_raid_target,
            twitch_raid_suggestions,
            twitch_raid_target,
            twitch_raid_state,
            start_twitch_raid,
            cancel_twitch_raid,
            acknowledge_twitch_raid,
            chat_catalog_status,
            refresh_chat_catalogs,
            activate_spotify_device,
            spotify_query,
            spotify_action,
            music_automation_action,
            music_automation_status,
            music_state_action,
            music_state_snapshot,
            music_statistics_snapshot,
            music_player_snapshot,
            music_overlay_snapshot,
            music_player_action,
            music_player_disconnect,
            music_player_connect,
            ytm_connect,
            ytm_disconnect,
            ytm_command,
            ytm_now_playing,
            ytm_runtime_status,
            reset_music_statistics,
            set_spotify_playlist_favorite,
            startup_error,
            overlay_runtime_status,
            setup_overlay_source,
            obs_query,
            obs_set_scene,
            obs_control,
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
            list_extension_packs,
            import_extension_pack,
            uninstall_extension_pack,
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
fn music_state_history_profiles_and_backups_cross_native_ipc_and_survive_restart() {
    let dir = tempfile::tempdir().unwrap();
    let file = dir.path().join("import.json");
    std::fs::write(&file,json!({"FormatVersion":2,"Entries":["10:00:00 · Intro: Song gespeichert"],"SavedCount":7,"Custom":42}).to_string()).unwrap();
    let app = test_app(dir.path().into());
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let action = |action: Value| call(&window, "music_state_action", json!({"action":action}));
    action(json!({"action":"history_import","path":file.to_string_lossy()})).unwrap();
    let data = call(&window, "music_state_snapshot", json!({})).unwrap();
    assert_eq!(data["history"]["SavedCount"], 7);
    assert_eq!(data["history"]["Custom"], 42);
    action(json!({"action":"history_edit","entries":["10:00:00 · Intro: Song gespeichert"],"favorite":true,"note":"native","remove":false})).unwrap();
    let backup = action(json!({"action":"backup"})).unwrap()["id"]
        .as_str()
        .unwrap()
        .to_string();
    action(json!({"action":"profile_save","profile":{"Name":"Eigenes Profil","Entries":true,"MergeEntries":true}})).unwrap();
    let preview = action(json!({"action":"backup_preview","id":backup})).unwrap();
    assert_eq!(preview["unchanged"], 1);
    assert!(action(json!({"action":"restore","group":"Standard","fadeSeconds":301})).is_err());
    let export = dir.path().join("export.csv");
    action(json!({"action":"history_export","path":export.to_string_lossy(),"csv":true})).unwrap();
    assert!(std::fs::read_to_string(export).unwrap().contains("native"));
    let restarted = test_app(dir.path().into());
    let second = WebviewWindowBuilder::new(&restarted, "main", Default::default())
        .build()
        .unwrap();
    let doc = call(&second, "music_state_snapshot", json!({})).unwrap();
    assert_eq!(
        doc["history"]["Notes"]["10:00:00 · Intro: Song gespeichert"],
        "native"
    );
    assert_eq!(doc["profiles"].as_array().unwrap().len(), 4);
}

#[test]
fn music_scene_commands_decode_native_arguments_and_preserve_imported_rules() {
    let dir = tempfile::tempdir().unwrap();
    let player = Arc::new(SpotifyClient::with_http(
        Arc::new(ccs_secrets::MemorySecretStore::new()),
        ccs_modules::spotify::SpotifyOAuthClient::new(),
        "http://127.0.0.1:1",
    ));
    let app = test_app_with_spotify(dir.path().into(), Some(player));
    let window = WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let original = call(&window, "get_settings", json!({})).unwrap();
    let mut settings = original.clone();
    settings["Spotify"]["SmartAutomationEnabled"] = json!(false);
    settings["Spotify"]["AutomationRules"] = json!([{"Id":"imported","TriggerType":"ObsSceneChanged","TriggerValue":"Game","ActionType":"Pause","DelaySeconds":2,"Custom":{"keep":42}}]);
    call(
        &window,
        "save_settings",
        json!({"original":original,"settings":settings}),
    )
    .unwrap();
    assert!(call(
        &window,
        "music_automation_action",
        json!({"action":{"action":"scene","scene":"Game","force":false}})
    )
    .is_ok());
    assert!(call(
        &window,
        "music_automation_action",
        json!({"action":{"action":"fade_to","percent":101,"milliseconds":0,"pauseAtEnd":true}})
    )
    .is_err());
    assert!(call(
        &window,
        "music_automation_action",
        json!({"action":{"action":"stop"}})
    )
    .is_ok());
    let status = call(&window, "music_automation_status", json!({})).unwrap();
    assert_eq!(status["running"], false);
    assert!(status["history"]
        .as_array()
        .unwrap()
        .iter()
        .any(|r| r["success"] == false));
    let pending = tauri::async_runtime::block_on(async {
        let job = app.state::<AppState>().scene_music.dispatch(
            ccs_modules::scene_music::MusicAction::Scene {
                scene: "Game".into(),
                force: true,
            },
        );
        tokio::time::sleep(std::time::Duration::from_millis(30)).await;
        job
    });
    assert!(call(
        &window,
        "spotify_action",
        json!({"action":{"action":"next"}})
    )
    .is_err());
    let cancelled = tauri::async_runtime::block_on(pending)
        .unwrap()
        .unwrap_err()
        .to_string();
    assert_eq!(cancelled, "Musikaktion abgebrochen");
    let restarted = test_app(dir.path().into());
    let second = WebviewWindowBuilder::new(&restarted, "main", Default::default())
        .build()
        .unwrap();
    assert_eq!(
        call(&second, "get_settings", json!({})).unwrap()["Spotify"]["AutomationRules"],
        settings["Spotify"]["AutomationRules"]
    );
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
    tauri::async_runtime::block_on(async {
        Mock::given(method("PUT"))
            .respond_with(
                ResponseTemplate::new(204).set_delay(std::time::Duration::from_millis(100)),
            )
            .mount(&server)
            .await;
        let state = app.state::<AppState>();
        let saving = state.settings_mutation.lock().await;
        let concurrent_settings = async {
            for _ in 0..100 {
                if server
                    .received_requests()
                    .await
                    .unwrap()
                    .iter()
                    .any(|r| r.method.as_str() == "PUT")
                {
                    break;
                }
                tokio::time::sleep(std::time::Duration::from_millis(5)).await;
            }
            tokio::time::timeout(
                std::time::Duration::from_secs(1),
                state.scene_music.shutdown(),
            )
            .await
            .expect("playlist history persistence deadlocked with settings save");
            drop(saving);
        };
        let (result, ()) = tokio::join!(
            spotify_action(
                app.state::<AppState>(),
                ccs_modules::spotify::SpotifyAction::PlayPlaylist {
                    uri: "spotify:playlist:concurrent".into()
                }
            ),
            concurrent_settings
        );
        result.unwrap();
    });
}
pub(super) fn call(
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
    assert_eq!(
        call(
            &window,
            "notifications_snapshot",
            json!({"filter":"Fehler"})
        )
        .unwrap()["entries"]
            .as_array()
            .unwrap()
            .len(),
        1
    );
    let result = call(&window, "apply_profile", json!({"id":id,"original":next})).unwrap();
    assert_eq!(result["saved"], true);
    assert_eq!(
        call(&window, "notifications_snapshot", json!({"filter":"Info"})).unwrap()["entries"][0]
            ["message"],
        "Profil „Studio“ wurde angewendet."
    );
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
