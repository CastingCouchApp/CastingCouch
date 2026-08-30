use mime_guess::from_path;
use std::path::PathBuf;

pub fn canvas_root() -> PathBuf {
    if let Ok(p) = std::env::var("CCS_OVERLAY_ASSETS") {
        return PathBuf::from(p);
    }
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../../../src/CreatorControlSuite.Modules.Overlay/CanvasOverlay")
}

pub fn get_asset(asset_path: &str) -> Option<(Vec<u8>, String)> {
    let trimmed = asset_path.trim_start_matches('/');
    let path = canvas_root().join(trimmed);
    if !path.exists() || !path.is_file() {
        return None;
    }
    let bytes = std::fs::read(&path).ok()?;
    let mime = from_path(&path)
        .first_or_octet_stream()
        .essence_str()
        .to_string();
    Some((bytes, mime))
}

pub fn html_shell(kind: &str) -> Option<String> {
    let file = match kind {
        "editor" => "editor/index.html",
        "view" => "view/index.html",
        "solo" => "solo/index.html",
        _ => return None,
    };
    std::fs::read_to_string(canvas_root().join(file)).ok()
}

pub fn list_widget_types() -> Vec<String> {
    [
        "now-playing",
        "chat",
        "countdown",
        "viewer-count",
        "socials",
        "qr-code",
        "announcement-bar",
    ]
    .into_iter()
    .map(str::to_string)
    .collect()
}

pub fn list_shape_types() -> Vec<String> {
    ["rectangle", "ellipse", "divider"]
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
