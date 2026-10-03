use super::command_tests::{call, test_app_with_clients};
use super::*;
use futures_util::{SinkExt, StreamExt};
use tokio_tungstenite::tungstenite::Message;

#[test]
fn native_stream_start_sets_scene_first_and_rejects_unknown_active_and_failed_outputs() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app_with_clients(root.path().into(), None, None);
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let original = state.settings.read_value().await.unwrap();
        let mut next = original.clone();
        next["Obs"]["StartScene"] = json!("Intro");
        state.settings.save_edit(&original, &next).await.unwrap();
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let mode = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let peer_mode = mode.clone();
        let requests = Arc::new(std::sync::Mutex::new(Vec::<Value>::new()));
        let peer_requests = requests.clone();
        let peer = tokio::spawn(async move {
            let (socket, _) = listener.accept().await.unwrap();
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
            while let Some(Ok(Message::Text(text))) = ws.next().await {
                let request: Value = serde_json::from_str(&text).unwrap();
                let d = &request["d"];
                let kind = d["requestType"].as_str().unwrap();
                peer_requests.lock().unwrap().push(d.clone());
                let current = peer_mode.load(std::sync::atomic::Ordering::SeqCst);
                let failed = (current == 2 && kind == "SetCurrentProgramScene")
                    || (current == 3 && kind == "StartStream");
                let data = match kind {
                    "GetSceneList" => json!({"currentProgramSceneName":"Idle","scenes":[]}),
                    "GetStreamStatus" if current == 0 => json!({}),
                    "GetStreamStatus" => json!({"outputActive":current == 1}),
                    _ => json!({}),
                };
                if kind == "StartStream" && !failed {
                    peer_mode.store(1, std::sync::atomic::Ordering::SeqCst);
                    ws.send(Message::Text(json!({"op":5,"d":{"eventType":"StreamStateChanged","eventData":{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_STARTED"}}}).to_string().into())).await.unwrap();
                }
                ws.send(Message::Text(json!({"op":7,"d":{"requestId":d["requestId"],"requestType":kind,"requestStatus":{"result":!failed,"code":if failed {500}else{100},"comment":"start fixture failure"},"responseData":data}}).to_string().into())).await.unwrap();
            }
        });
        state
            .obs
            .connect_simple("127.0.0.1", port, None, false)
            .await
            .unwrap();
        let start = || {
            call(
                &window,
                "obs_control",
                json!({"control":{"action":"start_stream"}}),
            )
        };
        requests.lock().unwrap().clear();
        assert!(
            start().is_err(),
            "unknown status must not initiate a scene or stream mutation"
        );
        assert_eq!(
            requests
                .lock()
                .unwrap()
                .iter()
                .map(|d| d["requestType"].as_str().unwrap())
                .collect::<Vec<_>>(),
            vec!["GetStreamStatus"]
        );
        mode.store(1, std::sync::atomic::Ordering::SeqCst);
        assert!(
            start().is_err(),
            "already live must not reset the scene or start twice"
        );
        mode.store(2, std::sync::atomic::Ordering::SeqCst);
        requests.lock().unwrap().clear();
        assert!(
            start().is_err(),
            "a failed scene selection must prevent StartStream"
        );
        assert_eq!(
            requests
                .lock()
                .unwrap()
                .iter()
                .map(|d| d["requestType"].as_str().unwrap())
                .collect::<Vec<_>>(),
            vec!["GetStreamStatus", "SetCurrentProgramScene"]
        );
        assert!(call(&window, "stream_history_snapshot", json!({})).unwrap()["active"].is_null());
        mode.store(3, std::sync::atomic::Ordering::SeqCst);
        requests.lock().unwrap().clear();
        assert!(
            start().is_err(),
            "a rejected StartStream must remain an error"
        );
        assert!(call(&window, "stream_history_snapshot", json!({})).unwrap()["active"].is_null());
        mode.store(4, std::sync::atomic::Ordering::SeqCst);
        requests.lock().unwrap().clear();
        start().unwrap();
        let sent = requests.lock().unwrap().clone();
        assert_eq!(
            sent.iter()
                .map(|d| d["requestType"].as_str().unwrap())
                .collect::<Vec<_>>(),
            vec!["GetStreamStatus", "SetCurrentProgramScene", "StartStream"]
        );
        assert_eq!(sent[1]["requestData"]["sceneName"], "Intro");
        assert!(call(&window, "stream_history_snapshot", json!({})).unwrap()["active"].is_object());
        requests.lock().unwrap().clear();
        assert!(start().is_err());
        assert_eq!(requests.lock().unwrap().len(), 1);
        state.obs.disconnect().await.unwrap();
        peer.await.unwrap();
    });
}
