use super::ObsClient;
use crate::ModuleResult;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

#[derive(Debug, Clone, Deserialize, Serialize)]
#[serde(
    tag = "action",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum ObsControl {
    StartStream,
    StopStream,
    StartRecord,
    StopRecord,
    PauseRecord,
    ResumeRecord,
    StartReplayBuffer,
    StopReplayBuffer,
    SaveReplayBuffer,
    StartVirtualCam,
    StopVirtualCam,
    SetProfile {
        profile_name: String,
    },
    SetSceneCollection {
        scene_collection_name: String,
    },
    SetTransition {
        transition_name: String,
    },
    SetTransitionDuration {
        transition_duration: u32,
    },
    SetMute {
        input_name: String,
        input_muted: bool,
    },
    SetVolume {
        input_name: String,
        input_volume_db: f64,
    },
    SetMonitor {
        input_name: String,
        monitor_type: String,
    },
    SetSyncOffset {
        input_name: String,
        input_audio_sync_offset: i64,
    },
    SetVisibility {
        scene_name: String,
        scene_item_id: i64,
        scene_item_enabled: bool,
    },
    SetLocked {
        scene_name: String,
        scene_item_id: i64,
        scene_item_locked: bool,
    },
    SetIndex {
        scene_name: String,
        scene_item_id: i64,
        scene_item_index: u32,
    },
    SetTransform {
        scene_name: String,
        scene_item_id: i64,
        scene_item_transform: Value,
    },
    SetFilter {
        source_name: String,
        filter_name: String,
        filter_enabled: bool,
    },
    SetInputSettings {
        input_name: String,
        input_settings: Value,
    },
}

impl ObsControl {
    pub fn request(&self) -> (&'static str, Value) {
        let name = match self {
            Self::StartStream => "StartStream",
            Self::StopStream => "StopStream",
            Self::StartRecord => "StartRecord",
            Self::StopRecord => "StopRecord",
            Self::PauseRecord => "PauseRecord",
            Self::ResumeRecord => "ResumeRecord",
            Self::StartReplayBuffer => "StartReplayBuffer",
            Self::StopReplayBuffer => "StopReplayBuffer",
            Self::SaveReplayBuffer => "SaveReplayBuffer",
            Self::StartVirtualCam => "StartVirtualCam",
            Self::StopVirtualCam => "StopVirtualCam",
            Self::SetProfile { .. } => "SetCurrentProfile",
            Self::SetSceneCollection { .. } => "SetCurrentSceneCollection",
            Self::SetTransition { .. } => "SetCurrentSceneTransition",
            Self::SetTransitionDuration { .. } => "SetCurrentSceneTransitionDuration",
            Self::SetMute { .. } => "SetInputMute",
            Self::SetVolume { .. } => "SetInputVolume",
            Self::SetMonitor { .. } => "SetInputAudioMonitorType",
            Self::SetSyncOffset { .. } => "SetInputAudioSyncOffset",
            Self::SetVisibility { .. } => "SetSceneItemEnabled",
            Self::SetLocked { .. } => "SetSceneItemLocked",
            Self::SetIndex { .. } => "SetSceneItemIndex",
            Self::SetTransform { .. } => "SetSceneItemTransform",
            Self::SetFilter { .. } => "SetSourceFilterEnabled",
            Self::SetInputSettings { .. } => "SetInputSettings",
        };
        let mut data = serde_json::to_value(self).expect("serializable OBS control");
        data.as_object_mut().unwrap().remove("action");
        (name, data)
    }
}

impl ObsClient {
    pub async fn control(&self, control: ObsControl) -> ModuleResult<Value> {
        let (name, data) = control.request();
        self.send_request(name, Some(data)).await
    }
    pub async fn output_status(&self) -> ModuleResult<Value> {
        let (stream, record, replay, camera, stats) = tokio::try_join!(
            self.send_request("GetStreamStatus", None),
            self.send_request("GetRecordStatus", None),
            self.send_request("GetReplayBufferStatus", None),
            self.send_request("GetVirtualCamStatus", None),
            self.send_request("GetStats", None),
        )?;
        Ok(json!({"stream":stream,"record":record,"replay":replay,"camera":camera,"stats":stats}))
    }
    pub async fn video_settings(&self) -> ModuleResult<Value> {
        let mut value = self.send_request("GetVideoSettings", None).await?;
        value["connected"] = json!(true);
        Ok(value)
    }
    pub async fn preview_png(&self) -> ModuleResult<Vec<u8>> {
        use base64::Engine;
        let scene = self
            .current_program_scene()
            .await
            .ok_or_else(|| crate::ModuleError::Message("Keine OBS-Szene aktiv".into()))?;
        let value = self
            .send_request(
                "GetSourceScreenshot",
                Some(json!({"sourceName":scene,"imageFormat":"png","imageWidth":960})),
            )
            .await?;
        let encoded = value["imageData"]
            .as_str()
            .and_then(|s| s.strip_prefix("data:image/png;base64,"))
            .ok_or_else(|| crate::ModuleError::Message("OBS lieferte kein PNG".into()))?;
        base64::engine::general_purpose::STANDARD
            .decode(encoded)
            .map_err(|e| crate::ModuleError::Message(e.to_string()))
    }
}

impl ccs_overlay_server::ObsOverlayProvider for ObsClient {
    fn video_settings(&self) -> ccs_overlay_server::OverlayFuture<'_, Value> {
        Box::pin(async { self.video_settings().await.map_err(|e| e.to_string()) })
    }
    fn preview(&self) -> ccs_overlay_server::OverlayFuture<'_, Vec<u8>> {
        Box::pin(async { self.preview_png().await.map_err(|e| e.to_string()) })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn controls_use_obs_v5_request_fields() {
        let control: ObsControl = serde_json::from_value(json!({"action":"set_visibility","sceneName":"Live","sceneItemId":42,"sceneItemEnabled":false})).unwrap();
        let (name, data) = control.request();
        assert_eq!(name, "SetSceneItemEnabled");
        assert_eq!(
            data,
            json!({"sceneName":"Live","sceneItemId":42,"sceneItemEnabled":false})
        );
        assert_eq!(
            ObsControl::StartRecord.request(),
            ("StartRecord", json!({}))
        );
        assert!(
            serde_json::from_value::<ObsControl>(json!({"action":"ExecuteArbitrary"})).is_err()
        );
    }
}
