use mime_guess::from_path;
use std::path::PathBuf;

include!(concat!(env!("OUT_DIR"), "/canvas_assets.rs"));

pub fn get_asset(asset_path: &str) -> Option<(Vec<u8>, String)> {
    let safe = asset_path
        .split('/')
        .all(|p| !p.is_empty() && p != "." && p != ".." && !p.contains(['\\', ':']));
    if !safe {
        return None;
    }
    // A checkout override is deliberately limited to debug builds.
    if cfg!(debug_assertions) {
        if let Ok(root) = std::env::var("CCS_OVERLAY_ASSETS") {
            let path = PathBuf::from(root).join(asset_path);
            if let Ok(bytes) = std::fs::read(path) {
                return Some((
                    bytes,
                    from_path(asset_path)
                        .first_or_octet_stream()
                        .essence_str()
                        .to_string(),
                ));
            }
        }
    }
    embedded_asset(asset_path)
}

pub fn html_shell(kind: &str) -> Option<String> {
    let file = match kind {
        "editor" => "editor/index.html",
        "view" => "view/index.html",
        "solo" => "solo/index.html",
        _ => return None,
    };
    String::from_utf8(get_asset(file)?.0).ok()
}

pub fn list_widget_types() -> Vec<String> {
    [
        "online",
        "alert",
        "music",
        "chat",
        "ending-stats",
        "text",
        "image",
        "countdown",
        "socials",
        "partner-roulette",
        "goal-bar",
        "event-ticker",
        "viewer-count",
        "lower-third",
        "qr-code",
        "brb-panel",
        "announcement-bar",
        "animated-background",
        "bubatz-cantina",
        "fruppis-landadel",
    ]
    .into_iter()
    .map(str::to_string)
    .collect()
}

pub fn list_shape_types() -> Vec<String> {
    [
        "frame",
        "frame.card",
        "shape.vignette",
        "shape.scene-bg",
        "shape.cutout",
        "shape.divider",
        "shape.cam-ring",
        "shape.sticker",
    ]
    .into_iter()
    .map(str::to_string)
    .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lists_known_widgets() {
        assert!(list_widget_types().contains(&"chat".to_string()));
    }
}
