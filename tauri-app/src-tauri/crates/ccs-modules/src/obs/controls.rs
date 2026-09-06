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
    /// Explicit user action: create a browser source or update its URL and dimensions.
    /// Existing scene-item visibility, transforms, filters and other input settings survive.
    pub async fn ensure_overlay_source(
        &self,
        scene: &str,
        input: &str,
        url: &str,
        width: u32,
        height: u32,
    ) -> ModuleResult<Value> {
        if scene.trim().is_empty()
            || input.trim().is_empty()
            || !(1..=16384).contains(&width)
            || !(1..=16384).contains(&height)
        {
            return Err(crate::ModuleError::Message(
                "Szene, Quellenname und gültige Canvas-Größe sind erforderlich.".into(),
            ));
        }
        let address =
            url::Url::parse(url).map_err(|e| crate::ModuleError::Message(e.to_string()))?;
        if address.scheme() != "http"
            || address.host_str() != Some("127.0.0.1")
            || !address.path().starts_with("/view/")
        {
            return Err(crate::ModuleError::Message(
                "Lokale Canvas-URL erwartet.".into(),
            ));
        }
        let scenes = self.get_scene_list().await?;
        if !scenes.iter().any(|s| s.name == scene) {
            return Err(crate::ModuleError::Message(
                "OBS-Zielszene existiert nicht.".into(),
            ));
        }
        let inputs = self.send_request("GetInputList", None).await?;
        let entries = inputs["inputs"]
            .as_array()
            .ok_or_else(|| crate::ModuleError::Message("Ungültige OBS-Quellenliste".into()))?;
        let settings = json!({"url":url,"width":width,"height":height});
        if let Some(existing) = entries.iter().find(|i| i["inputName"] == input) {
            if existing["inputKind"] != "browser_source"
                && existing["unversionedInputKind"] != "browser_source"
            {
                return Err(crate::ModuleError::Message(
                    "Dieser Quellenname gehört zu einer anderen Quellenart.".into(),
                ));
            }
            let items = self
                .send_request("GetSceneItemList", Some(json!({"sceneName":scene})))
                .await?;
            let items = items["sceneItems"].as_array().ok_or_else(|| {
                crate::ModuleError::Message("Ungültige OBS-Szenenelemente".into())
            })?;
            self.send_request(
                "SetInputSettings",
                Some(json!({"inputName":input,"inputSettings":settings,"overlay":true})),
            )
            .await?;
            let attached = items.iter().any(|i| i["sourceName"] == input);
            if !attached {
                self.send_request(
                    "CreateSceneItem",
                    Some(json!({"sceneName":scene,"sourceName":input,"sceneItemEnabled":true})),
                )
                .await?;
            }
            Ok(
                json!({"created":false,"attached":!attached,"inputName":input,"sceneName":scene,"url":url}),
            )
        } else {
            self.send_request("CreateInput",Some(json!({"sceneName":scene,"inputName":input,"inputKind":"browser_source","inputSettings":settings,"sceneItemEnabled":true}))).await?;
            Ok(
                json!({"created":true,"attached":true,"inputName":input,"sceneName":scene,"url":url}),
            )
        }
    }
    pub async fn output_status(&self) -> ModuleResult<Value> {
        let (stream, record, replay, camera, stats) = tokio::join!(
            self.send_request("GetStreamStatus", None),
            self.send_request("GetRecordStatus", None),
            self.send_request("GetReplayBufferStatus", None),
            self.send_request("GetVirtualCamStatus", None),
            self.send_request("GetStats", None),
        );
        let stream = stream?;
        let mut result = json!({"stream":stream,"errors":{}});
        for (key, response) in [
            ("record", record),
            ("replay", replay),
            ("camera", camera),
            ("stats", stats),
        ] {
            match response {
                Ok(value) => result[key] = value,
                Err(error) => {
                    result[key] = Value::Null;
                    result["errors"][key] = json!(error.to_string());
                }
            }
        }
        Ok(result)
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
