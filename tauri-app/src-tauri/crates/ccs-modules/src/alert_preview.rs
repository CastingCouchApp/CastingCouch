use crate::alerts::{test_variables, validate_definition, AlertDefinition};
use base64::{engine::general_purpose::STANDARD, Engine};
use serde_json::{json, Value};
use std::{io::Read, path::Path};

const MAX_PREVIEW_BYTES: u64 = 64 * 1024 * 1024;

pub fn preview(definition: &AlertDefinition, user: &str) -> Result<Value, String> {
    validate_definition(definition)?;
    Ok(json!({
        "text": crate::alert_renderer::render_text(&definition.text_template, user, &test_variables(&definition.type_name)),
        "media": media(&definition.media_path)?,
        "sound": media(&definition.sound_path)?,
    }))
}

fn media(path: &str) -> Result<Value, String> {
    if path.trim().is_empty() {
        return Ok(Value::Null);
    }
    let path = ccs_core::paths::expand_path(path);
    let extension = Path::new(&path)
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    let mime = match extension.as_str() {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "mp4" | "m4v" => "video/mp4",
        "webm" => "video/webm",
        "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "ogg" => "audio/ogg",
        "flac" => "audio/flac",
        "m4a" => "audio/mp4",
        _ => {
            return Err(
                "Dieser Dateityp wird von der lokalen Medienvorschau nicht unterstützt.".into(),
            )
        }
    };
    let file = std::fs::File::open(&path)
        .map_err(|error| format!("Lokale Vorschau: {}: {error}", path.display()))?;
    if file.metadata().map_err(|error| error.to_string())?.len() > MAX_PREVIEW_BYTES {
        return Err("Die Vorschau unterstützt Dateien bis 64 MB.".into());
    }
    let mut bytes = Vec::new();
    file.take(MAX_PREVIEW_BYTES + 1)
        .read_to_end(&mut bytes)
        .map_err(|error| error.to_string())?;
    if bytes.len() as u64 > MAX_PREVIEW_BYTES {
        return Err("Die Vorschau unterstützt Dateien bis 64 MB.".into());
    }
    Ok(json!({"url":format!("data:{mime};base64,{}",STANDARD.encode(bytes)),"mime":mime}))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn preview_reads_local_sound_and_renders_the_same_template_as_playback() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("sound.wav");
        std::fs::write(&path, b"RIFFtest").unwrap();
        let result = preview(
            &AlertDefinition {
                type_name: "ReSub".into(),
                text_template: "{USER}: {months}".into(),
                sound_path: path.to_string_lossy().into(),
                ..Default::default()
            },
            "Alice",
        )
        .unwrap();
        assert_eq!(result["text"], "Alice: 12");
        assert_eq!(result["sound"]["url"], "data:audio/wav;base64,UklGRnRlc3Q=");
        assert!(result["media"].is_null());
    }
    #[test]
    fn missing_files_unsupported_formats_and_oversized_previews_are_errors() {
        let dir = tempfile::tempdir().unwrap();
        let missing = dir.path().join("missing.png");
        assert!(media(missing.to_str().unwrap()).is_err());
        assert!(media("document.html").is_err());
        let oversized = dir.path().join("large.mp4");
        std::fs::File::create(&oversized)
            .unwrap()
            .set_len(MAX_PREVIEW_BYTES + 1)
            .unwrap();
        assert!(media(oversized.to_str().unwrap())
            .unwrap_err()
            .contains("64 MB"));
    }
}
