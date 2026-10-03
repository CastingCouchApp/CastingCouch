use super::command_tests::{call, test_app_with_clients};
use super::*;
use ccs_modules::twitch::{
    TwitchConnectOptions, TwitchOAuthClient, TwitchTokenRepository, TwitchTokenSet,
};
use ccs_secrets::MemorySecretStore;
use futures_util::{SinkExt, StreamExt};
use tauri::Listener;
use tokio_tungstenite::tungstenite::Message;
use wiremock::{
    matchers::{method, path, query_param},
    Mock, MockServer, ResponseTemplate,
};

async fn wait_phase(runtime: &ccs_modules::stream_end::StreamEndRuntime, phase: &str) {
    tokio::time::timeout(std::time::Duration::from_secs(4), async {
        loop {
            if runtime.snapshot().await.phase == phase {
                break;
            }
            tokio::time::sleep(std::time::Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap_or_else(|_| panic!("Phase {phase} nicht erreicht"));
}
fn raid(from: &str, to: &str, direction: &str, timestamp: chrono::DateTime<chrono::Utc>) -> Value {
    let condition = if direction == "outgoing" {
        json!({"from_broadcaster_user_id":"owner"})
    } else {
        json!({"to_broadcaster_user_id":"owner"})
    };
    json!({"metadata":{"message_type":"notification","message_id":uuid::Uuid::new_v4().to_string(),"message_timestamp":timestamp.to_rfc3339()},"payload":{"subscription":{"type":"channel.raid","condition":condition},"event":{"from_broadcaster_user_id":from,"from_broadcaster_user_login":from,"to_broadcaster_user_id":to,"to_broadcaster_user_login":to,"viewers":4}}})
}

#[test]
fn native_raid_waits_for_actual_outgoing_proof_and_exposes_subscription_failure_and_recovery() {
    tauri::async_runtime::block_on(async {
        let http = MockServer::start().await;
        let client_id = "abcdefghijabcdefghijabcdefghij";
        Mock::given(path("/validate")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"client_id":client_id,"login":"moderator","user_id":"moderator","expires_in":3600,"scopes":[]}))).mount(&http).await;
        Mock::given(path("/users"))
            .respond_with(ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"id":"moderator","login":"moderator","display_name":"Moderator"}]}),
            ))
            .with_priority(2)
            .mount(&http)
            .await;
        for login in ["owner", "target"] {
            Mock::given(path("/users"))
                .and(query_param("login", login))
                .respond_with(ResponseTemplate::new(200).set_body_json(
                    json!({"data":[{"id":login,"login":login,"display_name":login}]}),
                ))
                .with_priority(1)
                .mount(&http)
                .await;
        }
        Mock::given(path("/streams")).respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{"user_id":"target","user_login":"target","user_name":"Target","viewer_count":10}]}))).mount(&http).await;
        let outgoing_denied = Arc::new(std::sync::atomic::AtomicBool::new(true));
        let deny = outgoing_denied.clone();
        Mock::given(method("POST"))
            .and(path("/eventsub/subscriptions"))
            .respond_with(move |request: &wiremock::Request| {
                let body: Value = serde_json::from_slice(&request.body).unwrap();
                if deny.load(std::sync::atomic::Ordering::SeqCst)
                    && body["condition"]["from_broadcaster_user_id"] == "owner"
                {
                    ResponseTemplate::new(403)
                        .set_body_json(json!({"message":"Outgoing raid denied"}))
                } else {
                    ResponseTemplate::new(202).set_body_json(json!({"data":[]}))
                }
            })
            .mount(&http)
            .await;
        Mock::given(method("POST"))
            .and(path("/raids"))
            .and(query_param("from_broadcaster_id", "owner"))
            .and(query_param("to_broadcaster_id", "target"))
            .respond_with(
                ResponseTemplate::new(200).set_body_json(json!({"data":[{"created_at":"now"}]})),
            )
            .mount(&http)
            .await;
        let event_listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let event_url = format!("ws://{}", event_listener.local_addr().unwrap());
        let (send, mut notifications) = tokio::sync::mpsc::unbounded_channel::<Value>();
        let event_task = tokio::spawn(async move {
            let mut session = 0;
            loop {
                let Ok((socket, _)) = event_listener.accept().await else {
                    break;
                };
                let mut ws = tokio_tungstenite::accept_async(socket).await.unwrap();
                session += 1;
                ws.send(Message::Text(json!({"metadata":{"message_type":"session_welcome"},"payload":{"session":{"id":format!("session-{session}")}}}).to_string().into())).await.unwrap();
                loop {
                    tokio::select! {
                        message=notifications.recv()=>{let Some(message)=message else{return;};
                            if message.is_null() {let _=ws.close(None).await;break;}
                            if ws.send(Message::Text(message.to_string().into())).await.is_err(){break;}
                        },
                        frame=ws.next()=>{if !matches!(frame,Some(Ok(_))) {break;}}
                    }
                }
            }
        });
        let secrets = Arc::new(MemorySecretStore::new());
        TwitchTokenRepository::new(secrets.clone())
            .save(&TwitchTokenSet::from_oauth(
                "token".into(),
                "refresh".into(),
                3600,
                vec![],
            ))
            .unwrap();
        let twitch = Arc::new(TwitchClient::with_http_and_eventsub(
            secrets,
            TwitchOAuthClient::with_base_urls(
                format!("{}/device", http.uri()),
                format!("{}/token", http.uri()),
                format!("{}/validate", http.uri()),
            ),
            format!("{}/", http.uri()),
            event_url,
        ));
        let options = TwitchConnectOptions {
            client_id: client_id.into(),
            channel_name: "owner".into(),
            scopes: vec![],
            enable_event_sub: true,
        };
        twitch.connect(&options).await.unwrap();
        let root = tempfile::tempdir().unwrap();
        let app = test_app_with_clients(root.path().into(), None, Some(twitch.clone()));
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let obs_listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = obs_listener.local_addr().unwrap().port();
        let stops = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let requested_stops = stops.clone();
        let connections = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let accepted = connections.clone();
        let (close_obs, mut obs_disconnect) = tokio::sync::mpsc::unbounded_channel::<()>();
        let obs_task = tokio::spawn(async move {
            let mut active = false;
            for _ in 0..2 {
                let (socket, _) = obs_listener.accept().await.unwrap();
                accepted.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                let mut ws = tokio_tungstenite::accept_async(socket).await.unwrap();
                ws.send(Message::Text(
                    json!({"op":0,"d":{"rpcVersion":1,"obsWebSocketVersion":"5.6.0"}})
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
                loop {
                    let frame = tokio::select! {frame=ws.next()=>frame,_=obs_disconnect.recv()=>{let _=ws.close(None).await;break;}};
                    let Some(Ok(Message::Text(text))) = frame else {
                        break;
                    };
                    let request: Value = serde_json::from_str(&text).unwrap();
                    let kind = request["d"]["requestType"].as_str().unwrap();
                    if kind == "StopStream" {
                        active = false;
                        requested_stops.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                    }
                    if kind == "StartStream" {
                        active = true;
                    }
                    if matches!(kind, "StartStream" | "StopStream") {
                        ws.send(Message::Text(json!({"op":5,"d":{"eventType":"StreamStateChanged","eventData":{"outputActive":active,"outputState":if active {"OBS_WEBSOCKET_OUTPUT_STARTED"}else{"OBS_WEBSOCKET_OUTPUT_STOPPED"}}}}).to_string().into())).await.unwrap();
                    }
                    let data = match kind {
                        "GetSceneList" => json!({"currentProgramSceneName":"Live","scenes":[]}),
                        "GetStreamStatus" => json!({"outputActive":active,"outputDuration":1000}),
                        _ => json!({}),
                    };
                    ws.send(Message::Text(json!({"op":7,"d":{"requestId":request["d"]["requestId"],"requestType":kind,"requestStatus":{"result":true,"code":100},"responseData":data}}).to_string().into())).await.unwrap();
                }
            }
        });
        state
            .obs
            .connect_simple("127.0.0.1", port, None, false)
            .await
            .unwrap();
        let mut settings = state.settings.read_value().await.unwrap();
        settings["Obs"]["Host"] = json!("127.0.0.1");
        settings["Obs"]["Port"] = json!(port);
        settings["General"]["ConnectionWatchdogEnabled"] = json!(false);
        settings["Twitch"]["ClientId"] = json!(client_id);
        settings["Twitch"]["ChannelName"] = json!("owner");
        settings["Twitch"]["EnableEventSub"] = json!(true);
        settings["Twitch"]["StreamEndMode"] = json!("EndSceneRaidThenStop");
        settings["Twitch"]["SelectedRaidChannel"] = json!("target");
        settings["Twitch"]["RaidCountdownSeconds"] = json!(5);
        settings["Twitch"]["StopSpotifyAfterRaid"] = json!(false);
        settings["Spotify"]["PauseOnStreamEnd"] = json!(false);
        state
            .settings
            .save_edit(&state.settings.read_value().await.unwrap(), &settings)
            .await
            .unwrap();
        spawn_live_event_bridges(
            app.handle().clone(),
            state.obs.clone(),
            twitch.clone(),
            state.spotify.clone(),
            state.bridge.clone(),
            state.alerts.clone(),
        );
        stream_end_host::spawn_events(app.handle().clone(), state.stream_end.clone());
        let (events, mut observed) = tokio::sync::mpsc::unbounded_channel();
        app.listen("twitch-event", move |event| {
            let _ = events.send(serde_json::from_str::<Value>(event.payload()).unwrap());
        });
        call(
            &window,
            "obs_control",
            json!({"control":{"action":"start_stream"}}),
        )
        .unwrap();
        assert!(
            !call(&window, "stream_end_snapshot", json!({})).unwrap()["outgoingRaid"]["available"]
                .as_bool()
                .unwrap()
        );
        let error = call(&window, "start_stream_end", json!({"planned":false})).unwrap_err();
        assert!(
            error.as_str().unwrap().contains("Outgoing raid denied"),
            "{error}"
        );
        assert!(!http
            .received_requests()
            .await
            .unwrap()
            .iter()
            .any(|r| r.url.path() == "/raids"));
        outgoing_denied.store(false, std::sync::atomic::Ordering::SeqCst);
        twitch.connect(&options).await.unwrap();
        assert!(twitch.outgoing_raid_subscription().await.available);
        let reviewed = call(&window, "stream_end_snapshot", json!({})).unwrap()["original"].clone();
        let mut stale = reviewed.clone();
        stale["Obs"]["EndScene"] = json!("Different");
        assert!(call(
            &window,
            "start_stream_end",
            json!({"planned":false,"reviewed":stale})
        )
        .unwrap_err()
        .as_str()
        .unwrap()
        .contains("inzwischen geändert"));
        call(
            &window,
            "start_stream_end",
            json!({"planned":false,"reviewed":reviewed}),
        )
        .unwrap();
        wait_phase(&state.stream_end, "raid_countdown").await;
        assert_eq!(state.stream_end.snapshot().await.broadcaster_id, "owner");
        assert!(call(&window, "start_twitch_raid", json!({"login":"target"})).is_err());
        let old = chrono::Utc::now() - chrono::Duration::minutes(1);
        for message in [
            raid("stranger", "owner", "incoming", chrono::Utc::now()),
            raid("owner", "wrong", "outgoing", chrono::Utc::now()),
            raid("owner", "target", "outgoing", old),
        ] {
            send.send(message).unwrap();
            tokio::time::timeout(std::time::Duration::from_secs(3), observed.recv())
                .await
                .unwrap()
                .unwrap();
            assert_eq!(stops.load(std::sync::atomic::Ordering::SeqCst), 0);
        }
        tokio::time::sleep(std::time::Duration::from_millis(5100)).await;
        assert_eq!(stops.load(std::sync::atomic::Ordering::SeqCst), 0);
        assert!(state.stream_end.snapshot().await.active);
        close_obs.send(()).unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(3), async {
            while state.obs.status().await.state == ccs_modules::ConnectionState::Connected {
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
        send.send(raid("owner", "target", "outgoing", chrono::Utc::now()))
            .unwrap();
        let actual = tokio::time::timeout(std::time::Duration::from_secs(3), observed.recv())
            .await
            .unwrap()
            .unwrap();
        assert_eq!(actual["type"], "channel.raid.outgoing");
        wait_phase(&state.stream_end, "completed").await;
        assert_eq!(stops.load(std::sync::atomic::Ordering::SeqCst), 1);
        assert_eq!(connections.load(std::sync::atomic::Ordering::SeqCst), 2);
        assert!(!state.stream_end.snapshot().await.raid_pending);
        let history = call(&window, "stream_history_snapshot", json!({})).unwrap();
        assert!(history["active"].is_null());
        assert_eq!(history["sessions"][0]["IncomingRaids"], 1);
        assert!(
            call(&window, "twitch_raid_state", json!({})).unwrap()["requestedTarget"].is_null()
        );
        send.send(Value::Null).unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(3), async {
            while twitch.outgoing_raid_subscription().await.available {
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
        assert!(
            call(&window, "stream_end_snapshot", json!({})).unwrap()["outgoingRaid"]["error"]
                .is_string()
        );
        twitch.connect(&options).await.unwrap();
        assert!(twitch.outgoing_raid_subscription().await.available);
        let requests = http.received_requests().await.unwrap();
        let outgoing = requests
            .iter()
            .filter(|r| {
                r.url.path() == "/eventsub/subscriptions"
                    && serde_json::from_slice::<Value>(&r.body).unwrap()["condition"]
                        ["from_broadcaster_user_id"]
                        == "owner"
            })
            .count();
        assert_eq!(outgoing, 3);
        assert_eq!(
            requests
                .iter()
                .filter(|r| r.method == "POST" && r.url.path() == "/raids")
                .count(),
            1
        );
        state.obs.disconnect().await.unwrap();
        twitch.logout().await.unwrap();
        obs_task.abort();
        event_task.abort();
    });
}
