use ccs_core::AppSettings;
use ccs_modules::{
    alerts::{AlertEngine, MemorySettingsStore},
    obs::{ObsClient, ObsControl, ObsQuery},
    overlay_bridge::OverlayEventBridge,
};
use ccs_overlay_server::RealtimeHub;
use futures_util::{SinkExt, StreamExt};
use serde_json::{json, Value};
use std::sync::{Arc, Mutex};
use tokio_tungstenite::tungstenite::Message;

async fn server(
    fail: &str,
) -> (
    Arc<ObsClient>,
    Arc<Mutex<Vec<Value>>>,
    tokio::task::JoinHandle<()>,
) {
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let port = listener.local_addr().unwrap().port();
    let fail = fail.to_string();
    let requests = Arc::new(Mutex::new(Vec::new()));
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
            let req: Value = serde_json::from_str(&text).unwrap();
            let d = &req["d"];
            let kind = d["requestType"].as_str().unwrap_or("");
            recorded.lock().unwrap().push(d.clone());
            let failed = kind == fail
                || (fail == "HideSources"
                    && kind == "SetSceneItemEnabled"
                    && d["requestData"]["sceneItemEnabled"] == false);
            let data = match kind {
                "GetSceneList" => {
                    json!({"currentProgramSceneName":"Live","scenes":[{"sceneName":"Live","sceneIndex":0},{"sceneName":"_alerts","sceneIndex":1}]})
                }
                "GetStreamStatus" => json!({"outputActive":true,"outputDuration":120000}),
                "GetRecordStatus" | "GetReplayBufferStatus" | "GetVirtualCamStatus" => {
                    json!({"outputActive":false})
                }
                "GetInputList" => {
                    json!({"inputs":[{"inputName":"Existing","inputKind":"browser_source"},{"inputName":"Wrong kind","inputKind":"ffmpeg_source"}]})
                }
                "GetSceneItemList" => {
                    json!({"sceneItems":[{"sourceName":"Existing","sceneItemId":20,"sceneItemEnabled":false}]})
                }
                "GetProfileList" => {
                    json!({"currentProfileName":"Streaming","profiles":[{"profileName":"Streaming"}]})
                }
                "GetSceneCollectionList" => {
                    json!({"currentSceneCollectionName":"Main","sceneCollections":[{"sceneCollectionName":"Main"}]})
                }
                "GetSceneItemTransform" => {
                    json!({"sceneItemTransform":{"positionX":12,"positionY":20}})
                }
                "GetInputAudioSyncOffset" => json!({"inputAudioSyncOffset":120}),
                "GetInputAudioMonitorType" => json!({"monitorType":"OBS_MONITORING_TYPE_NONE"}),
                "GetInputMute" => json!({"inputMuted":false}),
                "GetInputVolume" => json!({"inputVolumeDb":-12,"inputVolumeMul":0.25}),
                "GetStats" => json!({"activeFps":60}),
                "GetSceneItemId" => {
                    json!({"sceneItemId":if d["requestData"]["sourceName"]=="_alert_text" {1}else{2}})
                }
                _ => json!({}),
            };
            ws.send(Message::Text(json!({"op":7,"d":{"requestType":kind,"requestId":d["requestId"],"requestStatus":{"result":!failed,"code":if failed {500}else{100},"comment":if failed {"contract failure"}else{""}},"responseData":data}}).to_string().into())).await.unwrap();
        }
    });
    let obs = ObsClient::new_shared("127.0.0.1", port);
    obs.connect_simple("127.0.0.1", port, None, false)
        .await
        .unwrap();
    (obs, requests, task)
}
#[tokio::test]
async fn optional_output_failure_keeps_stream_status_and_exposes_error() {
    let (obs, _, task) = server("GetVirtualCamStatus").await;
    let status = obs.output_status().await.unwrap();
    assert_eq!(status["stream"]["outputActive"], true);
    assert!(status["camera"].is_null());
    assert!(status["errors"]["camera"]
        .as_str()
        .unwrap()
        .contains("contract failure"));
    obs.disconnect().await.unwrap();
    task.await.unwrap();
}
#[tokio::test]
async fn alert_stop_hides_existing_sources_without_creating_them() {
    let (obs, requests, task) = server("").await;
    let mut settings = AppSettings::default();
    settings.alerts.inter_alert_delay_milliseconds = 0;
    settings
        .alerts
        .definitions
        .get_mut("Follow")
        .unwrap()
        .duration_seconds = 60;
    settings
        .alerts
        .definitions
        .get_mut("Follow")
        .unwrap()
        .media_path = "contract.mp4".into();
    let engine = AlertEngine::from_memory(
        Arc::new(MemorySettingsStore::new(settings)),
        OverlayEventBridge::new(Arc::new(RealtimeHub::new())),
    );
    engine.attach_obs(obs.clone());
    engine.test_alert("Follow", "Alice").await.unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(3), async {
        loop {
            if requests.lock().unwrap().iter().any(|r| {
                r["requestType"] == "TriggerMediaInputAction"
                    && r["requestData"]["mediaAction"] == "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART"
            }) {
                break;
            }
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    engine.stop_current();
    tokio::time::timeout(std::time::Duration::from_secs(3), async {
        while engine.pending_count() != 0 {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    let commands = requests.lock().unwrap().clone();
    assert!(!commands
        .iter()
        .any(|r| r["requestType"] == "CreateInput" || r["requestType"] == "CreateScene"));
    assert!(commands
        .iter()
        .any(|r| r["requestType"] == "TriggerMediaInputAction"
            && r["requestData"]["mediaAction"] == "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP"));
    assert_eq!(
        commands
            .iter()
            .filter(|r| r["requestType"] == "SetSceneItemEnabled"
                && r["requestData"]["sceneItemEnabled"] == false)
            .count(),
        2
    );
    assert!(engine.runtime().await.unwrap().last_error.is_none());
    obs.disconnect().await.unwrap();
    task.await.unwrap();
}

#[tokio::test]
async fn overlay_source_creation_and_update_preserve_existing_items() {
    let (obs, requests, task) = server("").await;
    obs.ensure_overlay_source(
        "Live",
        "New overlay",
        "http://127.0.0.1:8765/view/default",
        1920,
        1080,
    )
    .await
    .unwrap();
    obs.ensure_overlay_source(
        "Live",
        "Existing",
        "http://127.0.0.1:9999/view/default",
        1280,
        720,
    )
    .await
    .unwrap();
    let commands = requests.lock().unwrap().clone();
    let create = commands
        .iter()
        .find(|r| r["requestType"] == "CreateInput")
        .unwrap();
    assert_eq!(create["requestData"]["inputKind"], "browser_source");
    assert_eq!(create["requestData"]["inputSettings"]["width"], 1920);
    let update = commands
        .iter()
        .find(|r| r["requestType"] == "SetInputSettings")
        .unwrap();
    assert_eq!(
        update["requestData"]["inputSettings"]["url"],
        "http://127.0.0.1:9999/view/default"
    );
    assert_eq!(update["requestData"]["overlay"], true);
    assert!(!commands.iter().any(
        |r| r["requestType"] == "CreateSceneItem" || r["requestType"] == "SetSceneItemEnabled"
    ));
    assert!(obs
        .ensure_overlay_source(
            "Live",
            "Wrong kind",
            "http://127.0.0.1:8765/view/default",
            1920,
            1080
        )
        .await
        .is_err());
    assert_eq!(
        requests
            .lock()
            .unwrap()
            .iter()
            .filter(|r| r["requestType"] == "SetInputSettings")
            .count(),
        1
    );
    obs.disconnect().await.unwrap();
    task.await.unwrap();
}

#[tokio::test]
async fn alert_cleanup_errors_are_visible_after_playback_finishes() {
    let (obs, _, task) = server("HideSources").await;
    let mut settings = AppSettings::default();
    settings.alerts.inter_alert_delay_milliseconds = 0;
    settings
        .alerts
        .definitions
        .get_mut("Follow")
        .unwrap()
        .duration_seconds = 0;
    let engine = AlertEngine::from_memory(
        Arc::new(MemorySettingsStore::new(settings)),
        OverlayEventBridge::new(Arc::new(RealtimeHub::new())),
    );
    engine.attach_obs(obs.clone());
    engine.test_alert("Follow", "Alice").await.unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(3), async {
        while engine.pending_count() != 0 {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    assert!(engine
        .runtime()
        .await
        .unwrap()
        .last_error
        .unwrap()
        .contains("contract failure"));
    obs.disconnect().await.unwrap();
    task.await.unwrap();
}

#[tokio::test]
async fn management_queries_and_audio_controls_roundtrip_obs_fields() {
    let (obs, requests, task) = server("").await;
    assert_eq!(
        obs.query(ObsQuery::Profiles).await.unwrap()["currentProfileName"],
        "Streaming"
    );
    assert_eq!(
        obs.query(ObsQuery::SceneCollections).await.unwrap()["sceneCollections"][0]
            ["sceneCollectionName"],
        "Main"
    );
    assert_eq!(
        obs.query(ObsQuery::Transform {
            scene_name: "Live".into(),
            scene_item_id: 42
        })
        .await
        .unwrap()["sceneItemTransform"]["positionX"],
        12
    );
    let query: ObsQuery =
        serde_json::from_value(json!({"query":"audio_sync_offset","inputName":"Mic"})).unwrap();
    assert_eq!(obs.query(query).await.unwrap()["inputAudioSyncOffset"], 120);
    obs.control(ObsControl::SetMonitor {
        input_name: "Mic".into(),
        monitor_type: "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT".into(),
    })
    .await
    .unwrap();
    obs.control(ObsControl::SetSyncOffset {
        input_name: "Mic".into(),
        input_audio_sync_offset: 120,
    })
    .await
    .unwrap();
    obs.control(ObsControl::SetInputSettings {
        input_name: "Existing".into(),
        input_settings: json!({"url":"http://127.0.0.1:8765/view/default"}),
    })
    .await
    .unwrap();
    let commands = requests.lock().unwrap().clone();
    assert!(commands
        .iter()
        .any(|r| r["requestType"] == "GetSceneItemTransform"
            && r["requestData"]["sceneName"] == "Live"
            && r["requestData"]["sceneItemId"] == 42));
    assert!(commands
        .iter()
        .any(|r| r["requestType"] == "SetInputSettings" && r["requestData"]["overlay"] == true));
    assert!(commands
        .iter()
        .any(|r| r["requestType"] == "GetInputAudioSyncOffset"
            && r["requestData"]["inputName"] == "Mic"));
    assert!(commands
        .iter()
        .any(|r| r["requestType"] == "SetInputAudioMonitorType"
            && r["requestData"]["monitorType"] == "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT"));
    assert!(obs
        .query(ObsQuery::SceneItems {
            scene_name: "".into()
        })
        .await
        .is_err());
    obs.disconnect().await.unwrap();
    task.await.unwrap();
}
#[tokio::test]
async fn failed_audio_query_remains_an_error_not_a_default_value() {
    let (obs, _, task) = server("GetInputMute").await;
    assert!(obs
        .query(ObsQuery::Mute {
            input_name: "Camera".into()
        })
        .await
        .unwrap_err()
        .to_string()
        .contains("contract failure"));
    obs.disconnect().await.unwrap();
    task.await.unwrap();
}
