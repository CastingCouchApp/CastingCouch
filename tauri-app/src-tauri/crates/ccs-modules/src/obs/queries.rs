use super::ObsClient;
use crate::{ModuleError, ModuleResult};
use serde::{Deserialize, Serialize};
use serde_json::Value;
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(
    tag = "query",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum ObsQuery {
    Transform {
        scene_name: String,
        scene_item_id: i64,
    },
    Profiles,
    SceneCollections,
    Transitions,
    CurrentTransition,
    Inputs,
    SceneItems {
        scene_name: String,
    },
    GroupItems {
        scene_name: String,
    },
    InputSettings {
        input_name: String,
    },
    Mute {
        input_name: String,
    },
    Volume {
        input_name: String,
    },
    AudioMonitor {
        input_name: String,
    },
    AudioSyncOffset {
        input_name: String,
    },
    Filters {
        source_name: String,
    },
    FilterSettings {
        source_name: String,
        filter_name: String,
    },
}
impl ObsQuery {
    pub fn request(&self) -> ModuleResult<(&'static str, Value)> {
        let name = match self {
            Self::Transform { .. } => "GetSceneItemTransform",
            Self::Profiles => "GetProfileList",
            Self::SceneCollections => "GetSceneCollectionList",
            Self::Transitions => "GetSceneTransitionList",
            Self::CurrentTransition => "GetCurrentSceneTransition",
            Self::Inputs => "GetInputList",
            Self::SceneItems { .. } => "GetSceneItemList",
            Self::GroupItems { .. } => "GetGroupSceneItemList",
            Self::InputSettings { .. } => "GetInputSettings",
            Self::Mute { .. } => "GetInputMute",
            Self::Volume { .. } => "GetInputVolume",
            Self::AudioMonitor { .. } => "GetInputAudioMonitorType",
            Self::AudioSyncOffset { .. } => "GetInputAudioSyncOffset",
            Self::Filters { .. } => "GetSourceFilterList",
            Self::FilterSettings { .. } => "GetSourceFilter",
        };
        let mut data = serde_json::to_value(self).expect("serializable query");
        let fields = data.as_object_mut().unwrap();
        fields.remove("query");
        if fields
            .values()
            .any(|v| v.as_str().is_some_and(|s| s.trim().is_empty()))
        {
            return Err(ModuleError::Message(
                "OBS-Abfrage benötigt einen gültigen Namen.".into(),
            ));
        }
        Ok((name, data))
    }
}
impl ObsClient {
    pub async fn query(&self, query: ObsQuery) -> ModuleResult<Value> {
        let (name, data) = query.request()?;
        self.send_request(name, Some(data)).await
    }
}
