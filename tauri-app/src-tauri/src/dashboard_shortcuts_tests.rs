use super::command_tests::{call, test_app_with_clients};
use super::*;
use futures_util::{SinkExt, StreamExt};
use tokio_tungstenite::tungstenite::Message;

#[test]
fn dashboard_scene_and_audio_commands_cross_actual_obs_and_journal_replies_and_errors() {
    tauri::async_runtime::block_on(async {
        let root = tempfile::tempdir().unwrap();
        let app = test_app_with_clients(root.path().into(), None, None);
        let window = WebviewWindowBuilder::new(&app, "main", Default::default())
            .build()
            .unwrap();
        let state = app.state::<AppState>();
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let fail = Arc::new(std::sync::atomic::AtomicBool::new(false));
        let peer_fail = fail.clone();
        let requests = Arc::new(std::sync::Mutex::new(Vec::<Value>::new()));
        let recorded = requests.clone();
        let task = tokio::spawn(async move {
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
                recorded.lock().unwrap().push(d.clone());
                let failed = peer_fail.load(std::sync::atomic::Ordering::SeqCst)
                    && matches!(kind, "SetInputMute" | "SetCurrentProgramScene");
                let data = match kind {
                    "GetSceneList" => {
                        json!({"currentProgramSceneName":"Live","scenes":[{"sceneName":"Live","sceneIndex":0}]})
                    }
                    "GetInputList" => {
                        json!({"inputs":[{"inputName":"Mic"},{"inputName":"Camera"}]})
                    }
                    "GetInputMute" => json!({"inputMuted":false}),
                    "GetInputVolume" => json!({"inputVolumeDb":-12.0}),
                    _ => json!({}),
                };
                ws.send(Message::Text(json!({"op":7,"d":{"requestId":d["requestId"],"requestType":kind,"requestStatus":{"result":!failed,"code":if failed{500}else{100},"comment":"shortcut fixture refusal"},"responseData":data}}).to_string().into())).await.unwrap();
            }
        });
        state
            .obs
            .connect_simple("127.0.0.1", port, None, false)
            .await
            .unwrap();
        assert_eq!(
            call(&window, "obs_query", json!({"query":{"query":"inputs"}})).unwrap()["inputs"][0]
                ["inputName"],
            "Mic"
        );
        assert_eq!(
            call(
                &window,
                "obs_query",
                json!({"query":{"query":"volume","inputName":"Mic"}})
            )
            .unwrap()["inputVolumeDb"],
            -12.0
        );
        call(&window, "obs_set_scene", json!({"scene":"Intro"})).unwrap();
        call(
            &window,
            "obs_control",
            json!({"control":{"action":"set_mute","inputName":"Mic","inputMuted":true}}),
        )
        .unwrap();
        call(
            &window,
            "obs_control",
            json!({"control":{"action":"set_volume","inputName":"Mic","inputVolumeDb":-6.0}}),
        )
        .unwrap();
        let info = call(&window, "notifications_snapshot", json!({"filter":"Info"})).unwrap();
        assert_eq!(info["total"], 3);
        assert!(info["entries"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["message"] == "OBS-Szene gewechselt: Intro"));
        assert!(info["entries"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["message"] == "Mic wurde gemutet."));
        fail.store(true, std::sync::atomic::Ordering::SeqCst);
        assert!(call(&window, "obs_set_scene", json!({"scene":"Gone"})).is_err());
        assert!(call(
            &window,
            "obs_control",
            json!({"control":{"action":"set_mute","inputName":"Mic","inputMuted":false}})
        )
        .is_err());
        let errors = call(
            &window,
            "notifications_snapshot",
            json!({"filter":"Fehler"}),
        )
        .unwrap();
        assert_eq!(errors["entries"].as_array().unwrap().len(), 2);
        assert!(errors["entries"]
            .as_array()
            .unwrap()
            .iter()
            .all(|e| e["message"]
                .as_str()
                .unwrap()
                .contains("shortcut fixture refusal")));
        assert!(!requests.lock().unwrap().iter().any(|d| matches!(
            d["requestType"].as_str(),
            Some("StartStream" | "StopStream")
        )));
        assert!(requests
            .lock()
            .unwrap()
            .iter()
            .any(|d| d["requestType"] == "SetInputVolume"
                && d["requestData"] == json!({"inputName":"Mic","inputVolumeDb":-6.0})));
        state.obs.disconnect().await.unwrap();
        task.await.unwrap();
        assert_eq!(
            ccs_modules::notifications::NotificationRuntime::new(root.path())
                .snapshot("Fehler")
                .unwrap()
                .entries
                .len(),
            2
        );
    });
}
