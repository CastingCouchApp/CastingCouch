use crate::{alerts::AlertDefinition, obs::ObsClient};
use ccs_core::AlertSettings;
use serde_json::{json, Value};
use std::collections::BTreeMap;

pub fn render_text(template: &str, user: &str, variables: &BTreeMap<String, String>) -> String {
    let mut output = String::new();
    let mut remaining = template;
    while let Some(start) = remaining.find('{') {
        output.push_str(&remaining[..start]);
        remaining = &remaining[start..];
        if let Some(end) = remaining.find('}') {
            let name = &remaining[1..end];
            if !name.is_empty() && name.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'_') {
                let value = if name.eq_ignore_ascii_case("user") {
                    Some(user)
                } else {
                    variables
                        .iter()
                        .find(|(key, _)| key.eq_ignore_ascii_case(name))
                        .map(|(_, v)| v.as_str())
                };
                output.push_str(value.unwrap_or(&remaining[..=end]));
                remaining = &remaining[end + 1..];
                continue;
            }
        }
        output.push('{');
        remaining = &remaining[1..];
    }
    output.push_str(remaining);
    output
}

fn color(value: &str) -> u32 {
    let rgb = u32::from_str_radix(value.trim_start_matches('#'), 16).unwrap_or(0xffffff);
    0xff000000 | ((rgb & 0xff) << 16) | (rgb & 0xff00) | ((rgb >> 16) & 0xff)
}
pub fn text_settings(def: &AlertDefinition, text: &str) -> Value {
    json!({"text":text,"color":color(&def.font_color),"font":{"face":def.font_face,"size":def.font_size,"style":"Regular","flags":0}})
}
async fn request(obs: &ObsClient, kind: &str, data: Value) -> Result<Value, String> {
    obs.send_request(kind, Some(data))
        .await
        .map_err(|e| e.to_string())
}
async fn item_id(obs: &ObsClient, scene: &str, input: &str) -> Result<i64, String> {
    request(
        obs,
        "GetSceneItemId",
        json!({"sceneName":scene,"sourceName":input}),
    )
    .await?["sceneItemId"]
        .as_i64()
        .ok_or_else(|| "OBS-Quelle fehlt. Bitte zuerst die Alert-Szene einrichten.".into())
}
async fn visible(obs: &ObsClient, scene: &str, input: &str, enabled: bool) -> Result<(), String> {
    let id = item_id(obs, scene, input).await?;
    request(
        obs,
        "SetSceneItemEnabled",
        json!({"sceneName":scene,"sceneItemId":id,"sceneItemEnabled":enabled}),
    )
    .await?;
    Ok(())
}

pub async fn install(
    obs: &ObsClient,
    settings: &AlertSettings,
    def: &AlertDefinition,
) -> Result<(), String> {
    let scenes = obs.get_scene_list().await.map_err(|e| e.to_string())?;
    if !scenes
        .iter()
        .any(|scene| scene.name == settings.obs_scene_name)
    {
        request(
            obs,
            "CreateScene",
            json!({"sceneName":settings.obs_scene_name}),
        )
        .await?;
    }
    let kinds = request(obs, "GetInputKindList", json!({})).await?;
    let text_kind = kinds["inputKinds"]
        .as_array()
        .and_then(|kinds| {
            kinds.iter().filter_map(Value::as_str).find(|name| {
                name.starts_with(if cfg!(target_os = "windows") {
                    "text_gdiplus"
                } else {
                    "text_ft2_source"
                })
            })
        })
        .ok_or("OBS-Textquellentyp ist nicht verfügbar")?;
    let inputs = request(obs, "GetInputList", json!({})).await?;
    for (name, kind, data) in [
        (
            &settings.obs_text_source_name,
            text_kind,
            text_settings(def, "Alert-Vorschau"),
        ),
        (
            &settings.obs_media_source_name,
            "ffmpeg_source",
            json!({"local_file":def.media_path,"is_local_file":true,"looping":false}),
        ),
    ] {
        if let Some(existing) = inputs["inputs"]
            .as_array()
            .and_then(|inputs| inputs.iter().find(|input| input["inputName"] == *name))
        {
            let existing_kind = existing["inputKind"].as_str().unwrap_or("");
            if existing_kind != kind {
                return Err(format!("OBS-Quelle {name} hat einen anderen Typ ({existing_kind}). Bitte einen anderen Quellennamen wählen."));
            }
            if item_id(obs, &settings.obs_scene_name, name).await.is_err() {
                request(obs,"CreateSceneItem",json!({"sceneName":settings.obs_scene_name,"sourceName":name,"sceneItemEnabled":false})).await?;
            }
        } else {
            request(obs,"CreateInput",json!({"sceneName":settings.obs_scene_name,"inputName":name,"inputKind":kind,"inputSettings":data,"sceneItemEnabled":false})).await?;
        }
    }
    Ok(())
}
pub async fn show(
    obs: &ObsClient,
    settings: &AlertSettings,
    def: &AlertDefinition,
    text: &str,
) -> Result<(), String> {
    let text_id = item_id(
        obs,
        &settings.obs_scene_name,
        &settings.obs_text_source_name,
    )
    .await?;
    request(obs,"SetInputSettings",json!({"inputName":settings.obs_text_source_name,"inputSettings":text_settings(def,text),"overlay":true})).await?;
    request(obs,"SetSceneItemTransform",json!({"sceneName":settings.obs_scene_name,"sceneItemId":text_id,"sceneItemTransform":{"positionX":def.x,"positionY":def.y,"boundsType":"OBS_BOUNDS_SCALE_INNER","boundsWidth":def.width.max(1),"boundsHeight":def.height.max(1)}})).await?;
    visible(
        obs,
        &settings.obs_scene_name,
        &settings.obs_text_source_name,
        true,
    )
    .await?;
    if !def.media_path.trim().is_empty() {
        request(obs,"SetInputSettings",json!({"inputName":settings.obs_media_source_name,"inputSettings":{"local_file":def.media_path,"is_local_file":true,"looping":false,"restart_on_activate":false,"close_when_inactive":true,"clear_on_media_end":true},"overlay":true})).await?;
        request(obs,"SetInputVolume",json!({"inputName":settings.obs_media_source_name,"inputVolumeMul":def.volume_percent.clamp(0,100) as f64/100.0})).await?;
        visible(
            obs,
            &settings.obs_scene_name,
            &settings.obs_media_source_name,
            true,
        )
        .await?;
        request(obs,"TriggerMediaInputAction",json!({"inputName":settings.obs_media_source_name,"mediaAction":"OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART"})).await?;
    }
    Ok(())
}
pub async fn hide(obs: &ObsClient, settings: &AlertSettings) -> Result<(), String> {
    // Attempt every cleanup action even if one source is unavailable.
    let mut errors = Vec::new();
    if let Err(e)=request(obs,"TriggerMediaInputAction",json!({"inputName":settings.obs_media_source_name,"mediaAction":"OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP"})).await {errors.push(e);}
    for name in [
        &settings.obs_media_source_name,
        &settings.obs_text_source_name,
    ] {
        if let Err(e) = visible(obs, &settings.obs_scene_name, name, false).await {
            errors.push(e);
        }
    }
    if errors.is_empty() {
        Ok(())
    } else {
        Err(format!(
            "Alert konnte nicht vollständig ausgeblendet werden: {}",
            errors.join("; ")
        ))
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn template_is_case_insensitive_and_does_not_expand_inserted_values() {
        assert_eq!(
            render_text(
                "{USER}: {BITS} {missing}",
                "{bits}",
                &BTreeMap::from([("bits".into(), "100".into())])
            ),
            "{bits}: 100 {missing}"
        );
    }
    #[test]
    fn alert_variables_and_obs_color_match_the_legacy_renderer() {
        assert_eq!(
            render_text(
                "{user}: {bits} Bits",
                "Alice",
                &BTreeMap::from([("bits".into(), "100".into())])
            ),
            "Alice: 100 Bits"
        );
        let def = AlertDefinition {
            font_color: "#123456".into(),
            font_size: 32,
            ..Default::default()
        };
        assert_eq!(text_settings(&def, "Hello")["color"], 0xff563412u32);
        assert_eq!(text_settings(&def, "Hello")["font"]["size"], 32);
    }
}
