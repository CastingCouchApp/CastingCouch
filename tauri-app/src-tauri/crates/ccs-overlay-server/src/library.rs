use crate::pack_manifest;
use serde_json::{json, Value};
use std::{
    fs,
    io::{Cursor, Read},
    path::{Path, PathBuf},
    sync::Mutex,
};

const IMAGE_LIMIT: usize = 15 * 1024 * 1024;
pub const PACK_LIMIT: usize = 50 * 1024 * 1024;
static LIBRARY_GATE: Mutex<()> = Mutex::new(());
type Result<T> = std::result::Result<T, String>;

pub struct MediaLibrary {
    root: PathBuf,
}

fn safe_segment(id: &str) -> bool {
    !id.is_empty()
        && id
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_')
}
fn safe_relative(path: &str) -> bool {
    pack_manifest::relative_path(path)
}

fn pack_files(root: &Path, relative: &Path, files: &mut Vec<String>) -> Result<()> {
    for entry in fs::read_dir(root.join(relative)).map_err(|e| e.to_string())? {
        let entry = entry.map_err(|e| e.to_string())?;
        let kind = entry.file_type().map_err(|e| e.to_string())?;
        let path = relative.join(entry.file_name());
        if kind.is_dir() {
            pack_files(root, &path, files)?;
        } else if kind.is_file() {
            files.push(
                path.to_str()
                    .ok_or("Ungültiger Dateiname")?
                    .replace('\\', "/"),
            );
        }
    }
    Ok(())
}
fn ext(path: &str) -> String {
    Path::new(path)
        .extension()
        .and_then(|v| v.to_str())
        .unwrap_or("")
        .to_ascii_lowercase()
}
fn read_json(path: &Path) -> Result<Value> {
    serde_json::from_slice(&fs::read(path).map_err(|e| e.to_string())?).map_err(|e| e.to_string())
}
fn atomic_json(path: &Path, data: &Value) -> Result<()> {
    let temp = path.with_extension(format!("{}.tmp", uuid::Uuid::new_v4().simple()));
    fs::write(
        &temp,
        serde_json::to_vec_pretty(data).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    fs::rename(&temp, path).map_err(|e| e.to_string())
}

impl MediaLibrary {
    pub fn new(root: impl AsRef<Path>) -> Self {
        Self {
            root: root.as_ref().to_path_buf(),
        }
    }
    fn index(&self) -> Result<Vec<Value>> {
        let path = self.root.join("assets/index.json");
        if !path.exists() {
            return Ok(vec![]);
        }
        let value = read_json(&path)?;
        value
            .get("assets")
            .unwrap_or(&value)
            .as_array()
            .cloned()
            .ok_or_else(|| "Ungültiger Asset-Index".into())
    }
    fn asset_info(record: &Value) -> Value {
        json!({"id":record["id"], "name":record["originalName"], "url":format!("/assets/{}",record["id"].as_str().unwrap_or("")), "contentType":record["contentType"], "size":record["sizeBytes"], "createdAt":record["createdAt"]})
    }
    pub fn assets(&self) -> Result<Vec<Value>> {
        let _guard = LIBRARY_GATE.lock().map_err(|e| e.to_string())?;
        Ok(self.index()?.iter().map(Self::asset_info).collect())
    }
    pub fn import_image(&self, name: &str, data: &[u8]) -> Result<Value> {
        let _guard = LIBRARY_GATE.lock().map_err(|e| e.to_string())?;
        let extension = ext(name);
        if !["png", "jpg", "jpeg", "webp", "gif", "bmp", "svg"].contains(&extension.as_str())
            || data.is_empty()
            || data.len() > IMAGE_LIMIT
        {
            return Err("Bilddatei erwartet (png/jpg/webp/gif/bmp/svg, maximal 15 MB).".into());
        }
        let dir = self.root.join("assets");
        fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        let mut index = self.index()?;
        let id = uuid::Uuid::new_v4().simple().to_string();
        let file_name = format!("{id}.{extension}");
        let record = json!({"id":id,"fileName":file_name,"originalName":name.rsplit(['/', '\\']).next().unwrap_or(name),"contentType":mime_guess::from_path(name).first_or_octet_stream().essence_str(),"sizeBytes":data.len(),"createdAt":chrono::Utc::now().to_rfc3339()});
        fs::write(dir.join(&file_name), data).map_err(|e| e.to_string())?;
        index.push(record.clone());
        if let Err(e) = atomic_json(&dir.join("index.json"), &json!({"assets":index})) {
            let _ = fs::remove_file(dir.join(file_name));
            return Err(e);
        }
        Ok(Self::asset_info(&record))
    }
    pub fn asset_path(&self, id: &str) -> Result<PathBuf> {
        if !safe_segment(id) {
            return Err("Ungültige Asset-ID".into());
        }
        let _guard = LIBRARY_GATE.lock().map_err(|e| e.to_string())?;
        let index = self.index()?;
        let record = index
            .iter()
            .find(|r| r["id"].as_str().is_some_and(|v| v.eq_ignore_ascii_case(id)))
            .ok_or("Asset nicht gefunden")?;
        self.contained_file(
            &self.root.join("assets"),
            record["fileName"].as_str().ok_or("Dateiname fehlt")?,
        )
    }
    pub fn delete_asset(&self, id: &str) -> Result<()> {
        if !safe_segment(id) {
            return Err("Ungültige Asset-ID".into());
        }
        let _guard = LIBRARY_GATE.lock().map_err(|e| e.to_string())?;
        let mut index = self.index()?;
        if let Some(position) = index.iter().position(|v| v["id"].as_str() == Some(id)) {
            let record = index.remove(position);
            let file = self.contained_file(
                &self.root.join("assets"),
                record["fileName"].as_str().ok_or("Dateiname fehlt")?,
            )?;
            let tomb = file.with_extension("deleting");
            fs::rename(&file, &tomb).map_err(|e| e.to_string())?;
            if let Err(e) = atomic_json(
                &self.root.join("assets/index.json"),
                &json!({"assets":index}),
            ) {
                let _ = fs::rename(tomb, file);
                return Err(e);
            }
            fs::remove_file(tomb).map_err(|e| e.to_string())?;
        }
        Ok(())
    }
    pub fn packs(&self) -> Result<Vec<Value>> {
        let _guard = LIBRARY_GATE.lock().map_err(|e| e.to_string())?;
        let dir = self.root.join("extensions");
        if !dir.exists() {
            return Ok(vec![]);
        }
        let mut result = vec![];
        for entry in fs::read_dir(dir).map_err(|e| e.to_string())? {
            let path = entry.map_err(|e| e.to_string())?.path();
            if path
                .file_name()
                .and_then(|n| n.to_str())
                .is_some_and(pack_manifest::slug)
                && fs::symlink_metadata(&path)
                    .map_err(|e| e.to_string())?
                    .file_type()
                    .is_dir()
            {
                let parsed = (|| {
                    let mut files = Vec::new();
                    pack_files(&path, Path::new(""), &mut files)?;
                    let manifest_file = files
                        .iter()
                        .find(|name| name.eq_ignore_ascii_case("manifest.json"))
                        .ok_or("manifest.json fehlt")?;
                    let mut manifest =
                        pack_manifest::normalize(read_json(&path.join(manifest_file))?, &files)?;
                    let id = manifest["id"].as_str().unwrap();
                    if path.file_name().and_then(|n| n.to_str()) != Some(id) {
                        return Err("Pack-ID passt nicht zum Ordner".into());
                    }
                    manifest["baseUrl"] = json!(format!("/ext/{id}/"));
                    Ok::<_, String>(manifest)
                })();
                // Like C#, one damaged pack must not prevent loading the remaining catalog.
                if let Ok(manifest) = parsed {
                    result.push(manifest);
                }
            }
        }
        result.sort_by_key(|pack| {
            (
                pack["name"].as_str().unwrap_or("").to_lowercase(),
                pack["id"].as_str().unwrap_or("").to_string(),
            )
        });
        Ok(result)
    }
    pub fn extension_path(&self, id: &str, path: &str) -> Result<PathBuf> {
        if !safe_segment(id) {
            return Err("Ungültige Pack-ID".into());
        }
        self.contained_file(&self.root.join("extensions").join(id), path)
    }
    fn contained_file(&self, root: &Path, relative: &str) -> Result<PathBuf> {
        if !safe_relative(relative) {
            return Err("Ungültiger Dateipfad".into());
        }
        let root = fs::canonicalize(root).map_err(|e| e.to_string())?;
        let file = fs::canonicalize(root.join(relative)).map_err(|e| e.to_string())?;
        if !file.starts_with(&root) || !file.is_file() {
            return Err("Datei außerhalb der Bibliothek".into());
        }
        Ok(file)
    }
    pub fn install_pack(&self, data: &[u8]) -> Result<Value> {
        let _guard = LIBRARY_GATE.lock().map_err(|e| e.to_string())?;
        if data.len() > PACK_LIMIT {
            return Err("ZIP überschreitet 50 MB".into());
        }
        let mut archive = zip::ZipArchive::new(Cursor::new(data)).map_err(|e| e.to_string())?;
        let mut files = Vec::new();
        let mut total = 0u64;
        let mut seen = std::collections::HashSet::new();
        for i in 0..archive.len() {
            let mut entry = archive.by_index(i).map_err(|e| e.to_string())?;
            let normalized = entry.name().replace('\\', "/");
            let name = normalized.trim_end_matches('/').to_string();
            if !safe_relative(&name) || entry.unix_mode().is_some_and(|m| m & 0o170000 == 0o120000)
            {
                return Err("Unsicherer ZIP-Pfad".into());
            }
            if entry.is_dir() || normalized.ends_with('/') {
                continue;
            }
            total = total.checked_add(entry.size()).ok_or("ZIP zu groß")?;
            if total > PACK_LIMIT as u64 || !seen.insert(name.to_ascii_lowercase()) {
                return Err("ZIP zu groß oder doppelte Dateinamen".into());
            }
            if ![
                "js", "css", "woff2", "woff", "ttf", "otf", "svg", "png", "jpg", "jpeg", "webp",
                "gif", "json", "md",
            ]
            .contains(&ext(&name).as_str())
            {
                return Err("Nicht erlaubter Dateityp im ZIP".into());
            }
            let mut bytes = vec![];
            (&mut entry)
                .take(PACK_LIMIT as u64 + 1)
                .read_to_end(&mut bytes)
                .map_err(|e| e.to_string())?;
            if bytes.len() > PACK_LIMIT {
                return Err("ZIP zu groß".into());
            }
            files.push((name, bytes));
        }
        let manifest_index = files
            .iter()
            .position(|(name, _)| name.eq_ignore_ascii_case("manifest.json"))
            .ok_or("manifest.json fehlt")?;
        files[manifest_index].0 = "manifest.json".into();
        let manifest = pack_manifest::normalize(
            serde_json::from_slice(&files[manifest_index].1).map_err(|e| e.to_string())?,
            &files
                .iter()
                .map(|(name, _)| name.clone())
                .collect::<Vec<_>>(),
        )?;
        files[manifest_index].1 =
            serde_json::to_vec_pretty(&manifest).map_err(|e| e.to_string())?;
        let id = manifest["id"].as_str().unwrap();
        let root = self.root.join("extensions");
        fs::create_dir_all(&root).map_err(|e| e.to_string())?;
        let staging = root.join(format!(".installing-{}", uuid::Uuid::new_v4()));
        fs::create_dir(&staging).map_err(|e| e.to_string())?;
        let result = (|| {
            for (name, bytes) in &files {
                let path = staging.join(name);
                fs::create_dir_all(path.parent().unwrap()).map_err(|e| e.to_string())?;
                fs::write(path, bytes).map_err(|e| e.to_string())?;
            }
            let target = root.join(id);
            let backup = root.join(format!(".previous-{}", uuid::Uuid::new_v4()));
            let replaced = target.exists();
            if replaced {
                fs::rename(&target, &backup).map_err(|e| e.to_string())?;
            }
            if let Err(e) = fs::rename(&staging, &target) {
                if replaced {
                    let _ = fs::rename(&backup, &target);
                }
                return Err(e.to_string());
            }
            if replaced {
                let _ = fs::remove_dir_all(backup);
            }
            Ok(manifest.clone())
        })();
        if staging.exists() {
            let _ = fs::remove_dir_all(staging);
        }
        result
    }
    pub fn delete_pack(&self, id: &str) -> Result<()> {
        if !safe_segment(id) {
            return Err("Ungültige Pack-ID".into());
        }
        let _guard = LIBRARY_GATE.lock().map_err(|e| e.to_string())?;
        let root = self.root.join("extensions");
        let path = root.join(id);
        if path.exists() {
            let actual = fs::canonicalize(&path).map_err(|e| e.to_string())?;
            if !actual.starts_with(fs::canonicalize(root).map_err(|e| e.to_string())?) {
                return Err("Pack außerhalb der Bibliothek".into());
            }
            fs::remove_dir_all(path).map_err(|e| e.to_string())?;
        }
        Ok(())
    }
}
