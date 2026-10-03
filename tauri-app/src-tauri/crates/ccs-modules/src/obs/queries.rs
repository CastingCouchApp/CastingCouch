use super::ObsClient;
use crate::{ModuleError, ModuleResult};
use futures_util::{stream, StreamExt};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
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
    InputCatalog,
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
            Self::InputCatalog => {
                return Err(ModuleError::Message(
                    "Katalog benötigt mehrere OBS-Abfragen.".into(),
                ))
            }
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
        if matches!(query, ObsQuery::InputCatalog) {
            return self.input_catalog().await;
        }
        let (name, data) = query.request()?;
        self.send_request(name, Some(data)).await
    }

    async fn input_catalog(&self) -> ModuleResult<Value> {
        let list = self.send_request("GetInputList", None).await?;
        let entries = list["inputs"]
            .as_array()
            .ok_or_else(|| ModuleError::Message("Ungültige OBS-Quellenliste".into()))?;
        let mut inputs = Vec::with_capacity(entries.len());
        for entry in entries {
            let name = entry["inputName"]
                .as_str()
                .filter(|name| !name.trim().is_empty())
                .ok_or_else(|| ModuleError::Message("OBS-Quelle ohne gültigen Namen".into()))?;
            let kind = entry["inputKind"].as_str().unwrap_or_default();
            let unversioned = entry["unversionedInputKind"].as_str().unwrap_or_default();
            let mut input = entry.clone();
            input["category"] = json!(classify_input(name, kind, unversioned));
            input["inputMuted"] = Value::Null;
            input["muteError"] = Value::Null;
            inputs.push(input);
        }
        // Bound independent requests so one unavailable input does not serialize
        // every other source behind its timeout. Preserve the OBS catalog order.
        let mut inputs = stream::iter(inputs.into_iter().enumerate())
            .map(|(index, mut input)| async move {
                match self
                    .send_request(
                        "GetInputMute",
                        Some(json!({"inputName":input["inputName"]})),
                    )
                    .await
                {
                    Ok(state) => match state["inputMuted"].as_bool() {
                        Some(muted) => input["inputMuted"] = json!(muted),
                        None => input["muteError"] = json!("OBS-Mute-Zustand unbekannt"),
                    },
                    Err(error) => input["muteError"] = json!(error.to_string()),
                }
                (index, input)
            })
            .buffer_unordered(8)
            .collect::<Vec<_>>()
            .await;
        inputs.sort_by_key(|(index, _)| *index);
        let inputs = inputs
            .into_iter()
            .map(|(_, input)| input)
            .collect::<Vec<_>>();
        Ok(json!({"inputs":inputs}))
    }
}

// Same ordered name/kind heuristics as ClassifyObsAudioInput in the C# UI.
fn classify_input(name: &str, kind: &str, unversioned: &str) -> &'static str {
    let value = format!("{name} {kind} {unversioned}").to_lowercase();
    for (category, keywords) in [
        ("microphone", &["mic", "mikro", "yeti", "rode", "voice"][..]),
        ("music", &["spotify", "music", "musik"][..]),
        ("browser", &["browser", "alert", "streamelements"][..]),
    ] {
        if keywords.iter().any(|keyword| value.contains(keyword)) {
            return category;
        }
    }
    "game"
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn csharp_audio_categories_match_names_kinds_and_precedence() {
        for (name, kind, unversioned, expected) in [
            ("YETI", "", "", "microphone"),
            ("Mikrofon Musik", "browser_source", "", "microphone"),
            ("Spotify", "browser_source", "", "music"),
            ("", "", "browser_source", "browser"),
            ("StreamElements", "", "", "browser"),
            ("Desktop", "wasapi_output_capture", "", "game"),
        ] {
            assert_eq!(classify_input(name, kind, unversioned), expected);
        }
    }
}
