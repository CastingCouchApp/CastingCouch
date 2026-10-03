use ccs_core::settings::OverlayChatSettings;
use serde_json::{json, Value};
use std::path::PathBuf;

fn text(settings: &OverlayChatSettings, key: &str, default: &str) -> String {
    settings
        .extra
        .get(key)
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .unwrap_or(default)
        .to_owned()
}

fn number(settings: &OverlayChatSettings, key: &str, default: f64, min: f64, max: f64) -> f64 {
    settings
        .extra
        .get(key)
        .and_then(Value::as_f64)
        .unwrap_or(default)
        .clamp(min, max)
}

/// Read-only normalized projection: legacy/unknown settings remain lossless on disk.
pub(crate) fn background_path(settings: &OverlayChatSettings) -> Option<PathBuf> {
    if !text(settings, "BackgroundType", "None").eq_ignore_ascii_case("Image") {
        return None;
    }
    let path = text(settings, "BackgroundImagePath", "");
    if path.is_empty() {
        return None;
    }
    let path = ccs_core::paths::expand_path(&path);
    path.is_file().then_some(path)
}

pub(crate) fn config(settings: &OverlayChatSettings) -> Value {
    let image = background_path(settings);
    let kind = if image.is_some() {
        "Image"
    } else if text(settings, "BackgroundType", "None").eq_ignore_ascii_case("Color") {
        "Color"
    } else {
        "None"
    };
    let version = image
        .and_then(|p| std::fs::metadata(p).ok())
        .and_then(|m| m.modified().ok())
        .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|d| d.as_nanos().to_string())
        .unwrap_or_else(|| "0".into());
    json!({
        "enabled": settings.enabled,
        "showTwitchEvents": settings.show_twitch_events,
        "enableBttv": settings.enable_bttv,
        "enableFfz": settings.enable_ffz,
        "enableSevenTv": settings.enable_seven_tv,
        "maxBufferedMessages": settings.max_buffered_messages.clamp(0, 2000),
        "backgroundType": kind, "backgroundVersion": version,
        "backgroundColor": text(settings, "BackgroundColor", "#000000"),
        "backgroundOpacity": number(settings, "BackgroundOpacity", 0.55, 0.0, 1.0),
        "paddingPx": number(settings, "PaddingPx", 12.0, 0.0, 120.0) as i64,
        "borderRadiusPx": number(settings, "BorderRadiusPx", 12.0, 0.0, 64.0) as i64,
        "gapPx": number(settings, "GapPx", 6.0, 0.0, 48.0) as i64,
        "fontSizePx": number(settings, "FontSizePx", 18.0, 8.0, 72.0) as i64,
        "fontFamily": text(settings, "FontFamily", "Segoe UI, system-ui, sans-serif"),
    })
}
