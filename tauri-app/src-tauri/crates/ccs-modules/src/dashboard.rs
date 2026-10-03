//! C# dashboard settings adapter. Only edited, owned fields are written back.
use base64::{engine::general_purpose::STANDARD, Engine};
use serde::{Deserialize, Serialize};
use serde_json::{json, Map, Value};
use std::{collections::HashSet, io::Read};

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DashboardCardDraft {
    pub key: String,
    pub visible: bool,
    pub zone: String,
    pub size: String,
}
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SceneButtonDraft {
    pub id: String,
    pub title: String,
    pub scene_name: String,
    pub icon_kind: String,
    pub icon_value: String,
    pub color: String,
}
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DashboardPreferences {
    pub show_service_status: bool,
    pub show_stream_controls: bool,
    pub show_live_panels: bool,
    pub show_quick_services: bool,
    pub show_advanced_tools: bool,
    pub show_notifications: bool,
    pub show_stream_history: bool,
    pub auto_focus_mode_on_stream_start: bool,
    pub auto_exit_focus_mode_on_stream_end: bool,
    pub obs_scene_preview_size: String,
    pub dashboard_statistic: String,
    pub stream_end_expanded: bool,
}
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DashboardDraft {
    pub cards: Vec<DashboardCardDraft>,
    pub scene_buttons: Vec<SceneButtonDraft>,
    pub preferences: DashboardPreferences,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DashboardSnapshot {
    pub original: Value,
    pub draft: DashboardDraft,
    pub scene_choices: Vec<String>,
    pub warnings: Vec<String>,
}

pub const CARD_KEYS: &[&str] = &[
    "ConnectionStatus",
    "Community",
    "ObsSceneControl",
    "StreamControl",
    "StreamEnd",
    "Countdown",
    "Preflight",
    "SpotifyPlayer",
    "TwitchChat",
    "TwitchEvents",
    "StreamHistory",
    "CreatorIntelligence",
];
fn canonical(key: &str) -> &str {
    if key == "StreamStatistics" {
        "Community"
    } else {
        key
    }
}
fn strings(value: Option<&Value>, name: &str) -> Result<Vec<String>, String> {
    match value {
        None => Ok(Vec::new()),
        Some(v) => serde_json::from_value(v.clone())
            .map_err(|_| format!("Dashboard.{name}: Liste von Texten erwartet")),
    }
}
fn object(value: Option<&Value>, name: &str) -> Result<Map<String, Value>, String> {
    match value {
        None => Ok(Map::new()),
        Some(v) => v
            .as_object()
            .cloned()
            .ok_or_else(|| format!("Dashboard.{name}: Objekt erwartet")),
    }
}
fn dashboard(original: &Value) -> Result<Map<String, Value>, String> {
    if !original.is_object() {
        return Err("Einstellungen: Objekt erwartet".into());
    }
    object(original.get("Dashboard"), "Einstellungen")
}
fn size(v: &str) -> &str {
    match v {
        "Kompakt" | "Groß" => v,
        _ => "Standard",
    }
}
fn zone(v: &str) -> &str {
    match v {
        "Left" | "Right" => v,
        _ => "Center",
    }
}
fn default_zone(key: &str) -> &str {
    match key {
        "ConnectionStatus" | "Countdown" | "Preflight" => "Left",
        "SpotifyPlayer" | "TwitchEvents" | "CreatorIntelligence" => "Right",
        _ => "Center",
    }
}
fn mapped<'a>(values: &'a Map<String, Value>, key: &str) -> &'a str {
    values
        .get(key)
        .or_else(|| {
            if key == "Community" {
                values.get("StreamStatistics")
            } else {
                None
            }
        })
        .and_then(Value::as_str)
        .unwrap_or("")
}
pub fn snapshot(original: &Value) -> Result<DashboardSnapshot, String> {
    let raw = dashboard(original)?;
    let order = strings(raw.get("ModuleOrder"), "ModuleOrder")?;
    let hidden = strings(raw.get("HiddenModules"), "HiddenModules")?;
    let sizes = object(raw.get("ModuleSizes"), "ModuleSizes")?;
    let zones = object(raw.get("ModuleZones"), "ModuleZones")?;
    let mut keys = Vec::new();
    for key in order
        .iter()
        .map(|s| canonical(s))
        .chain(CARD_KEYS.iter().copied())
    {
        if CARD_KEYS.contains(&key) && !keys.contains(&key) {
            keys.push(key);
        }
    }
    let cards = keys
        .into_iter()
        .map(|key| DashboardCardDraft {
            key: key.into(),
            visible: !hidden.iter().any(|k| canonical(k) == key),
            size: size(mapped(&sizes, key)).into(),
            zone: zone(if mapped(&zones, key).is_empty() {
                default_zone(key)
            } else {
                mapped(&zones, key)
            })
            .into(),
        })
        .collect();
    let bool_value = |key: &str, default| raw.get(key).and_then(Value::as_bool).unwrap_or(default);
    let text = |key: &str, default: &str| {
        raw.get(key)
            .and_then(Value::as_str)
            .unwrap_or(default)
            .to_string()
    };
    let preferences = DashboardPreferences {
        show_service_status: bool_value("ShowServiceStatus", true),
        show_stream_controls: bool_value("ShowStreamControls", true),
        show_live_panels: bool_value("ShowLivePanels", true),
        show_quick_services: bool_value("ShowQuickServices", true),
        show_advanced_tools: bool_value("ShowAdvancedTools", true),
        show_notifications: bool_value("ShowNotifications", true),
        show_stream_history: bool_value("ShowStreamHistory", true),
        auto_focus_mode_on_stream_start: bool_value("AutoFocusModeOnStreamStart", false),
        auto_exit_focus_mode_on_stream_end: bool_value("AutoExitFocusModeOnStreamEnd", true),
        obs_scene_preview_size: size(&text("ObsScenePreviewSize", "Standard")).into(),
        dashboard_statistic: text("DashboardStatistic", "ViewerCount"),
        stream_end_expanded: bool_value("StreamEndExpanded", false),
    };
    let mut scene_buttons = Vec::new();
    let mut warnings = Vec::new();
    if let Some(value) = raw.get("SceneButtons") {
        let rows = value
            .as_array()
            .ok_or("Dashboard.SceneButtons: Liste erwartet")?;
        for (i, row) in rows.iter().enumerate() {
            let r = row
                .as_object()
                .ok_or("Dashboard.SceneButtons: Objekt erwartet")?;
            for field in ["Id", "Title", "SceneName", "IconKind", "IconValue", "Color"] {
                if r.get(field).is_some_and(|v| !v.is_string() && !v.is_null()) {
                    return Err(format!("Dashboard.SceneButtons.{field}: Text erwartet"));
                }
            }
            let get = |key: &str, default: &str| {
                r.get(key)
                    .and_then(Value::as_str)
                    .unwrap_or(default)
                    .to_string()
            };
            let scene_name = get("SceneName", "");
            let id = get("Id", &format!("legacy-{i}"));
            if scene_buttons.iter().any(|b: &SceneButtonDraft| b.id == id) {
                return Err("Dashboard.SceneButtons: doppelte ID".into());
            }
            let kind = get("IconKind", "Emoji");
            let icon_kind = match kind.to_ascii_lowercase().as_str() {
                "glyph" => "Glyph",
                "image" => "Image",
                "emoji"=>"Emoji",
                _ => {
                    warnings.push(format!("Szenenbutton {id}: unbekannter Symboltyp {kind}; wird beibehalten und als Video-Symbol angezeigt."));
                    kind.as_str()
                },
            }
            .to_string();
            let mut title = get("Title", "");
            if title.trim().is_empty() {
                title = scene_name.clone();
            }
            if scene_name.trim().is_empty() {
                warnings.push(format!(
                    "Szenenbutton {id} besitzt keine Szene. Bitte bearbeiten oder entfernen."
                ));
            }
            let color = get("Color", "");
            scene_buttons.push(SceneButtonDraft {
                id,
                title,
                scene_name,
                icon_kind,
                icon_value: get("IconValue", "🎬"),
                color,
            });
        }
    }
    let mut scene_choices = Vec::new();
    for key in ["StartScene", "LiveScene", "PauseScene", "EndScene"] {
        if let Some(name) = original["Obs"][key].as_str() {
            push_scene(&mut scene_choices, name);
        }
    }
    if let Some(rows) = original["Obs"]["AdditionalScenes"].as_array() {
        for name in rows.iter().filter_map(Value::as_str) {
            push_scene(&mut scene_choices, name);
        }
    }
    if scene_buttons.is_empty() && !bool_value("SceneButtonsInitialized", false) {
        for (key, icon) in [
            ("StartScene", "🚀"),
            ("LiveScene", "🎮"),
            ("PauseScene", "☕"),
            ("EndScene", "🏁"),
        ] {
            let name = original["Obs"][key].as_str().unwrap_or("").trim();
            if !name.is_empty()
                && !scene_buttons
                    .iter()
                    .any(|b| same_scene(&b.scene_name, name))
            {
                scene_buttons.push(SceneButtonDraft {
                    id: format!("configured-{key}"),
                    title: name.into(),
                    scene_name: name.into(),
                    icon_kind: "Emoji".into(),
                    icon_value: icon.into(),
                    color: String::new(),
                });
            }
        }
    }
    for button in &scene_buttons {
        push_scene(&mut scene_choices, &button.scene_name);
    }
    Ok(DashboardSnapshot {
        original: original.clone(),
        draft: DashboardDraft {
            cards,
            scene_buttons,
            preferences,
        },
        scene_choices,
        warnings,
    })
}
fn push_scene(names: &mut Vec<String>, name: &str) {
    let name = name.trim();
    if !name.is_empty() && !names.iter().any(|n| same_scene(n, name)) {
        names.push(name.into());
    }
}
fn same_scene(a: &str, b: &str) -> bool {
    a.to_lowercase() == b.to_lowercase()
}
fn button_fields(b: &SceneButtonDraft) -> [(&'static str, &str); 6] {
    [
        ("Id", &b.id),
        ("Title", &b.title),
        ("SceneName", &b.scene_name),
        ("IconKind", &b.icon_kind),
        ("IconValue", &b.icon_value),
        ("Color", &b.color),
    ]
}
pub fn apply(original: &Value, draft: &DashboardDraft) -> Result<Value, String> {
    let before = snapshot(original)?.draft;
    let mut raw = dashboard(original)?;
    let mut seen = HashSet::new();
    for c in &draft.cards {
        if !CARD_KEYS.contains(&c.key.as_str())
            || !seen.insert(&c.key)
            || size(&c.size) != c.size
            || zone(&c.zone) != c.zone
        {
            return Err("Ungültige Dashboard-Karte, Größe oder Spalte".into());
        }
    }
    if seen.len() != CARD_KEYS.len() {
        return Err("Dashboard-Karten sind unvollständig".into());
    }
    if draft.scene_buttons.len() > 100 {
        return Err("Maximal 100 Szenenbuttons".into());
    }
    let mut ids = HashSet::new();
    for b in &draft.scene_buttons {
        if b.id.trim().is_empty()
            || b.id.len() > 100
            || !ids.insert(&b.id)
            || b.scene_name.trim().is_empty()
            || b.scene_name.len() > 200
            || b.title.len() > 200
            || b.icon_value.len() > 4096
            || (!["Emoji", "Glyph", "Image"].contains(&b.icon_kind.as_str())
                && !before
                    .scene_buttons
                    .iter()
                    .any(|old| old.id == b.id && old.icon_kind == b.icon_kind))
            || (!b.color.is_empty()
                && !(b.color.len() == 7
                    && b.color.starts_with('#')
                    && b.color[1..].bytes().all(|c| c.is_ascii_hexdigit())))
        {
            return Err("Ungültiger Szenenbutton: ID, Szene, Symbol oder Farbe prüfen".into());
        }
        if b.icon_kind == "Image"
            && !before.scene_buttons.iter().any(|old| {
                old.id == b.id && old.icon_kind == b.icon_kind && old.icon_value == b.icon_value
            })
        {
            image_preview(&b.icon_value)?;
        }
    }
    let p = &draft.preferences;
    if size(&p.obs_scene_preview_size) != p.obs_scene_preview_size {
        return Err("Ungültige Vorschaugröße".into());
    }
    // Unknown legacy statistics remain intact until explicitly changed.
    if p.dashboard_statistic != before.preferences.dashboard_statistic
        && ![
            "ViewerCount",
            "FollowerCount",
            "SubscriberCount",
            "NewFollowers",
            "NewSubscribers",
            "ChatterCount",
        ]
        .contains(&p.dashboard_statistic.as_str())
    {
        return Err("Ungültige Dashboard-Statistik".into());
    }
    if draft
        .cards
        .iter()
        .map(|c| &c.key)
        .ne(before.cards.iter().map(|c| &c.key))
    {
        let old = strings(raw.get("ModuleOrder"), "ModuleOrder")?;
        let mut next = draft
            .cards
            .iter()
            .map(|c| c.key.clone())
            .collect::<Vec<_>>();
        next.extend(
            old.into_iter()
                .filter(|key| !CARD_KEYS.contains(&canonical(key))),
        );
        raw.insert("ModuleOrder".into(), json!(next));
    }
    let mut hidden = strings(raw.get("HiddenModules"), "HiddenModules")?;
    let mut hidden_changed = false;
    for card in &draft.cards {
        let previous = before.cards.iter().find(|c| c.key == card.key).unwrap();
        if card.visible != previous.visible {
            hidden.retain(|key| canonical(key) != card.key);
            if !card.visible {
                hidden.push(card.key.clone());
            }
            hidden_changed = true;
        }
        for (field, next, old) in [
            ("ModuleSizes", &card.size, &previous.size),
            ("ModuleZones", &card.zone, &previous.zone),
        ] {
            if next != old {
                let mut values = object(raw.get(field), field)?;
                values.insert(card.key.clone(), json!(next));
                raw.insert(field.into(), json!(values));
            }
        }
    }
    if hidden_changed {
        raw.insert("HiddenModules".into(), json!(hidden));
    }
    if draft.scene_buttons != before.scene_buttons {
        let old = raw
            .get("SceneButtons")
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default();
        let rows = draft
            .scene_buttons
            .iter()
            .map(|b| {
                let mut row = old
                    .iter()
                    .enumerate()
                    .find(|(i, r)| {
                        r["Id"]
                            .as_str()
                            .map(str::to_owned)
                            .unwrap_or_else(|| format!("legacy-{i}"))
                            == b.id
                    })
                    .and_then(|(_, r)| r.as_object())
                    .cloned()
                    .unwrap_or_default();
                let previous = before.scene_buttons.iter().find(|old| old.id == b.id);
                let existing = !row.is_empty();
                for (key, value) in button_fields(b) {
                    let unchanged = previous.is_some_and(|previous| {
                        button_fields(previous)
                            .iter()
                            .any(|(k, v)| *k == key && *v == value)
                    });
                    if !existing
                        || !unchanged
                        || (key == "Id" && !row.get("Id").is_some_and(Value::is_string))
                    {
                        let saved = if matches!(key, "Title" | "SceneName") {
                            value.trim()
                        } else {
                            value
                        };
                        row.insert(key.into(), json!(saved));
                    }
                }
                Value::Object(row)
            })
            .collect::<Vec<_>>();
        raw.insert("SceneButtons".into(), json!(rows));
        raw.insert("SceneButtonsInitialized".into(), json!(true));
    }
    let preferences = serde_json::to_value(&draft.preferences).map_err(|e| e.to_string())?;
    let old = serde_json::to_value(&before.preferences).map_err(|e| e.to_string())?;
    for (key, value) in preferences.as_object().unwrap() {
        if value != &old[key] {
            let mut chars = key.chars();
            let pascal = chars.next().unwrap().to_uppercase().to_string() + chars.as_str();
            raw.insert(pascal, value.clone());
        }
    }
    let mut edited = original.clone();
    if raw != dashboard(original)? {
        edited["Dashboard"] = Value::Object(raw);
    }
    Ok(edited)
}

pub fn image_preview(path: &str) -> Result<String, String> {
    let path = ccs_core::paths::expand_path(path);
    let mime = match path
        .extension()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_ascii_lowercase()
        .as_str()
    {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "bmp" => "image/bmp",
        _ => return Err("Szenensymbol muss PNG, JPEG, GIF, WebP oder BMP sein".into()),
    };
    let file =
        std::fs::File::open(&path).map_err(|e| format!("Szenensymbol {}: {e}", path.display()))?;
    const LIMIT: u64 = 15 * 1024 * 1024;
    if file.metadata().map_err(|e| e.to_string())?.len() > LIMIT {
        return Err("Szenensymbol ist größer als 15 MiB".into());
    }
    let mut bytes = Vec::new();
    file.take(LIMIT + 1)
        .read_to_end(&mut bytes)
        .map_err(|e| e.to_string())?;
    if bytes.len() as u64 > LIMIT {
        return Err("Szenensymbol ist größer als 15 MiB".into());
    }
    let valid = match mime {
        "image/png" => bytes.starts_with(b"\x89PNG\r\n\x1a\n"),
        "image/jpeg" => bytes.starts_with(b"\xff\xd8\xff"),
        "image/gif" => bytes.starts_with(b"GIF87a") || bytes.starts_with(b"GIF89a"),
        "image/bmp" => bytes.starts_with(b"BM"),
        _ => bytes.starts_with(b"RIFF") && bytes.get(8..12) == Some(b"WEBP"),
    };
    if !valid {
        return Err("Szenensymbol enthält kein Bild des angegebenen Formats".into());
    }
    Ok(format!("data:{mime};base64,{}", STANDARD.encode(bytes)))
}

pub async fn obs_preview(obs: &crate::obs::ObsClient) -> Result<Value, String> {
    let video = obs.video_settings().await.map_err(|e| e.to_string())?;
    let bytes = obs.preview_png().await.map_err(|e| e.to_string())?;
    Ok(
        json!({"url":format!("data:image/png;base64,{}",STANDARD.encode(bytes)),"width":video["baseWidth"],"height":video["baseHeight"]}),
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn stream_end_card_preserves_existing_hidden_order_and_expansion_preferences() {
        let original = json!({"Dashboard":{"ModuleOrder":["StreamEnd","Workflow","StreamControl"],"HiddenModules":["StreamEnd"],"StreamEndExpanded":true,"Future":7}});
        let mut draft = snapshot(&original).unwrap().draft;
        assert_eq!(draft.cards[0].key, "StreamEnd");
        assert!(!draft.cards[0].visible);
        assert!(draft.preferences.stream_end_expanded);
        assert_eq!(apply(&original, &draft).unwrap(), original);
        draft.cards[0].visible = true;
        let edited = apply(&original, &draft).unwrap();
        assert_eq!(
            edited["Dashboard"]["ModuleOrder"],
            original["Dashboard"]["ModuleOrder"]
        );
        assert_eq!(edited["Dashboard"]["Future"], 7);
        assert!(snapshot(&edited).unwrap().draft.cards[0].visible);
    }

    #[test]
    fn frontend_demo_fixture_matches_the_native_default_contract() {
        let fixture: Value = serde_json::from_str(include_str!(
            "../../../../src/features/dashboard/dashboard-default.json"
        ))
        .unwrap();
        assert_eq!(
            serde_json::to_value(snapshot(&json!({"Dashboard":{}})).unwrap().draft).unwrap(),
            fixture
        );
    }

    #[test]
    fn csharp_layout_aliases_and_excluded_modules_are_read_without_loss() {
        let original = json!({"Dashboard": {
            "ModuleOrder":["Workflow","StreamStatistics","TwitchEvents","Community","StreamDeckRemote"],
            "HiddenModules":["StreamStatistics","Workflow"],
            "ModuleSizes":{"StreamStatistics":"Groß","Workflow":"Kompakt"},
            "ModuleZones":{"StreamStatistics":"Right"},
            "ModuleWidths":{"Workflow":1220},"Future":{"keep":true},
            "ShowServiceStatus":false
        }});
        let snapshot = snapshot(&original).unwrap();
        assert_eq!(snapshot.original, original);
        assert_eq!(snapshot.draft.cards[0].key, "Community");
        assert!(!snapshot.draft.cards[0].visible);
        assert_eq!(snapshot.draft.cards[0].size, "Groß");
        assert_eq!(snapshot.draft.cards[0].zone, "Right");
        assert!(!snapshot.draft.cards.iter().any(|c| c.key == "Workflow"));
        assert!(!snapshot.draft.preferences.show_service_status);
        assert_eq!(apply(&original, &snapshot.draft).unwrap(), original);
    }

    #[test]
    fn scene_defaults_are_stable_and_intentionally_empty_list_stays_empty() {
        let original = json!({"Obs":{"StartScene":" Start ","LiveScene":"start","PauseScene":"Pause","EndScene":""}});
        let first = snapshot(&original).unwrap();
        assert_eq!(first.draft.scene_buttons.len(), 2);
        assert_eq!(
            first.draft.scene_buttons,
            snapshot(&original).unwrap().draft.scene_buttons
        );
        assert_eq!(first.draft.scene_buttons[0].scene_name, "Start");
        let mut draft = first.draft;
        draft.scene_buttons.clear();
        let saved = apply(&original, &draft).unwrap();
        assert_eq!(saved["Dashboard"]["SceneButtonsInitialized"], true);
        assert!(snapshot(&saved).unwrap().draft.scene_buttons.is_empty());
        let unicode = snapshot(&json!({"Obs":{"StartScene":"Ärger","LiveScene":"ärger"}})).unwrap();
        assert_eq!(unicode.draft.scene_buttons.len(), 1);
        assert_eq!(unicode.scene_choices.len(), 1);
    }

    #[test]
    fn edits_keep_button_ids_unknown_fields_and_unselected_configuration() {
        let original = json!({"Dashboard": {
            "ModuleOrder":["Workflow","StreamControl","ConnectionStatus"],
            "HiddenModules":["Automation"],
            "ModuleSizes":{"Workflow":"Groß"},
            "SceneButtons":[{"Id":"stable","Title":"Old","SceneName":"Live","IconKind":"Glyph","IconValue":"\u{e714}","Future":{"x":1}}],
            "ModuleHeights":{"StreamControl":350},"Future":7
        },"Branding":{"DisplayName":"Keep"}});
        let mut draft = snapshot(&original).unwrap().draft;
        draft.cards.reverse();
        let card = draft
            .cards
            .iter_mut()
            .find(|c| c.key == "StreamControl")
            .unwrap();
        card.visible = false;
        card.size = "Groß".into();
        draft.scene_buttons[0].title = "New".into();
        draft.scene_buttons[0].color = "#ff0011".into();
        let saved = apply(&original, &draft).unwrap();
        assert_eq!(saved["Dashboard"]["SceneButtons"][0]["Id"], "stable");
        assert_eq!(saved["Dashboard"]["SceneButtons"][0]["Future"]["x"], 1);
        assert_eq!(saved["Dashboard"]["SceneButtons"][0]["Title"], "New");
        assert_eq!(saved["Dashboard"]["ModuleSizes"]["Workflow"], "Groß");
        assert!(saved["Dashboard"]["HiddenModules"]
            .as_array()
            .unwrap()
            .contains(&json!("Automation")));
        assert!(saved["Dashboard"]["ModuleOrder"]
            .as_array()
            .unwrap()
            .contains(&json!("Workflow")));
        assert_eq!(
            saved["Dashboard"]["ModuleHeights"],
            original["Dashboard"]["ModuleHeights"]
        );
        assert_eq!(saved["Branding"], original["Branding"]);
    }

    #[test]
    fn invalid_drafts_and_unreadable_existing_data_cannot_be_saved() {
        let original = json!({"Dashboard":{}});
        let draft = snapshot(&original).unwrap().draft;
        let mut bad = draft.clone();
        bad.cards[0].size = "Huge".into();
        assert!(apply(&original, &bad).is_err());
        let mut bad = draft.clone();
        bad.cards[0].key = "Workflow".into();
        assert!(apply(&original, &bad).is_err());
        let mut bad = draft.clone();
        bad.cards.pop();
        assert!(apply(&original, &bad).is_err());
        let mut bad = draft.clone();
        bad.scene_buttons = vec![SceneButtonDraft {
            id: "a".into(),
            title: "Test".into(),
            scene_name: "".into(),
            icon_kind: "Emoji".into(),
            icon_value: "🎮".into(),
            color: "".into(),
        }];
        assert!(apply(&original, &bad).is_err());
        assert!(snapshot(&json!({"Dashboard":"future format"})).is_err());
        assert!(snapshot(&json!({"Dashboard":{"SceneButtons":["broken"]}})).is_err());
        assert!(snapshot(&json!({"Dashboard":{"ModuleSizes":"broken"}})).is_err());
        assert!(
            snapshot(&json!({"Dashboard":{"SceneButtons":[{"Id":"a","Title":{"future":1}}]}}))
                .is_err()
        );
    }

    #[test]
    fn editing_a_title_keeps_unknown_icon_kinds_and_untouched_field_representations() {
        let original = json!({"Dashboard":{"SceneButtons":[{"Id":"a","Title":"Old","SceneName":" Live ","IconKind":"FutureKind","IconValue":"future-value","Future":3}]}});
        let mut draft = snapshot(&original).unwrap().draft;
        draft.scene_buttons[0].title = "New".into();
        let saved = apply(&original, &draft).unwrap();
        assert_eq!(
            saved["Dashboard"]["SceneButtons"][0]["IconKind"],
            "FutureKind"
        );
        assert_eq!(saved["Dashboard"]["SceneButtons"][0]["SceneName"], " Live ");
        assert_eq!(saved["Dashboard"]["SceneButtons"][0]["Title"], "New");
    }

    #[test]
    fn images_are_bounded_raster_data_and_fail_visibly() {
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("icon.png");
        std::fs::write(&file, b"\x89PNG\r\n\x1a\nbody").unwrap();
        assert!(image_preview(file.to_str().unwrap())
            .unwrap()
            .starts_with("data:image/png;base64,"));
        assert!(image_preview(dir.path().join("missing.png").to_str().unwrap()).is_err());
        let wrong = dir.path().join("icon.svg");
        std::fs::write(&wrong, b"<svg/>").unwrap();
        assert!(image_preview(wrong.to_str().unwrap()).is_err());
        let huge = std::fs::File::create(&file).unwrap();
        huge.set_len(15 * 1024 * 1024 + 1).unwrap();
        assert!(image_preview(file.to_str().unwrap()).is_err());
    }
}
