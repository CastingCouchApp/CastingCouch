use serde_json::{json, Value};

type Result<T> = std::result::Result<T, String>;

fn normalize_fields(value: &mut Value, fields: &[&str]) -> Result<()> {
    let object = value.as_object_mut().ok_or("Manifest-Objekt erwartet")?;
    for field in fields {
        let matches: Vec<_> = object
            .keys()
            .filter(|key| key.eq_ignore_ascii_case(field))
            .cloned()
            .collect();
        if matches.len() > 1 {
            return Err(format!("Mehrdeutiges Manifest-Feld: {field}"));
        }
        if let Some(key) = matches.first() {
            let entry = object.remove(key).unwrap();
            object.insert((*field).into(), entry);
        }
    }
    Ok(())
}

fn required_text<'a>(value: &'a Value, field: &str) -> Result<&'a str> {
    value[field]
        .as_str()
        .filter(|text| !text.trim().is_empty())
        .ok_or_else(|| format!("Manifest-Feld fehlt oder ist ungültig: {field}"))
}

fn optional_text(value: &Value, field: &str) -> Result<()> {
    if !value[field].is_null() && !value[field].is_string() {
        return Err(format!("Text im Manifest-Feld {field} erwartet"));
    }
    Ok(())
}

pub(crate) fn relative_path(path: &str) -> bool {
    !path.is_empty()
        && !path.contains(['\\', ':', '\0'])
        && path
            .split('/')
            .all(|s| !s.is_empty() && s != "." && s != "..")
}

pub(crate) fn slug(id: &str) -> bool {
    !id.is_empty()
        && id
            .bytes()
            .all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'-')
}

fn reference(path: &str, files: &[String], extensions: &[&str]) -> Result<String> {
    let normalized = path.replace('\\', "/");
    if !relative_path(&normalized) {
        return Err(format!("Unsicherer Manifest-Dateipfad: {path}"));
    }
    let actual = files
        .iter()
        .find(|file| file.eq_ignore_ascii_case(&normalized))
        .ok_or_else(|| format!("Manifest referenziert fehlende Datei: {path}"))?;
    let extension = std::path::Path::new(actual)
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    if !extensions.is_empty() && !extensions.contains(&extension.as_str()) {
        return Err(format!("Dateityp passt nicht zum Manifest-Eintrag: {path}"));
    }
    Ok(actual.clone())
}

/// Normalize the case-insensitive C# document into the shared Canvas wire format.
/// Use actual archive paths so references also work on case-sensitive filesystems.
pub(crate) fn normalize(mut manifest: Value, files: &[String]) -> Result<Value> {
    normalize_fields(
        &mut manifest,
        &[
            "id",
            "name",
            "version",
            "apiVersion",
            "widgets",
            "effects",
            "animations",
            "fonts",
            "assets",
        ],
    )?;
    if !slug(required_text(&manifest, "id")?) || manifest["apiVersion"] != 1 {
        return Err("Ungültiges Pack-Manifest (Pack-ID und apiVersion 1 erforderlich)".into());
    }
    required_text(&manifest, "name")?;
    required_text(&manifest, "version")?;
    for kind in ["widgets", "effects", "animations", "fonts", "assets"] {
        if manifest[kind].is_null() {
            manifest[kind] = json!([]);
        }
        let entries = manifest[kind]
            .as_array_mut()
            .ok_or_else(|| format!("Manifest-Liste erwartet: {kind}"))?;
        let mut ids = std::collections::HashSet::new();
        for entry in entries {
            if kind == "assets" {
                *entry = json!(reference(
                    entry.as_str().ok_or("Asset-Dateipfad erwartet")?,
                    files,
                    &[]
                )?);
                continue;
            }
            if kind == "fonts" {
                normalize_fields(entry, &["family", "src", "weight", "style"])?;
                required_text(entry, "family")?;
                entry["src"] = json!(reference(
                    required_text(entry, "src")?,
                    files,
                    &["woff2", "woff", "ttf", "otf"]
                )?);
                optional_text(entry, "weight")?;
                optional_text(entry, "style")?;
            } else {
                normalize_fields(entry, &["id", "name", "entry", "css", "style"])?;
                let id = required_text(entry, "id")?;
                if !id
                    .bytes()
                    .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_')
                    || !ids.insert(id.to_string())
                {
                    return Err("Ungültige oder doppelte Modul-ID".into());
                }
                required_text(entry, "name")?;
                entry["entry"] = json!(reference(required_text(entry, "entry")?, files, &["js"])?);
                for field in ["css", "style"] {
                    optional_text(entry, field)?;
                    if let Some(path) = entry[field].as_str().filter(|path| !path.is_empty()) {
                        entry[field] = json!(reference(path, files, &["css"])?);
                    }
                }
            }
        }
    }
    Ok(manifest)
}
