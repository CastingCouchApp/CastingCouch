use crate::store::{normalize_settings, SettingsError};
use chrono::Utc;
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};
use std::path::{Path, PathBuf};
use tokio::{
    fs,
    io::{AsyncReadExt, AsyncWriteExt},
};
use uuid::Uuid;

const MAX_PROFILE_BYTES: usize = 16 * 1024 * 1024;
type Result<T> = std::result::Result<T, SettingsError>;
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct CreatorProfile {
    #[serde(alias = "id")]
    pub id: String,
    #[serde(alias = "name")]
    pub name: String,
    #[serde(default, alias = "description")]
    pub description: String,
    #[serde(alias = "createdAt")]
    pub created_at: String,
    #[serde(alias = "updatedAt")]
    pub updated_at: String,
    #[serde(alias = "settings")]
    pub settings: Value,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProfileSummary {
    pub id: String,
    pub name: String,
    pub description: String,
    pub updated_at: String,
}
#[derive(Debug, Serialize)]
pub struct ProfileList {
    pub profiles: Vec<ProfileSummary>,
    pub warnings: Vec<String>,
}
pub struct ProfileStore {
    root: PathBuf,
}
impl ProfileStore {
    pub fn new(root: impl Into<PathBuf>) -> Self {
        Self { root: root.into() }
    }
    fn path(&self, id: &str) -> Result<PathBuf> {
        if id.is_empty()
            || id.len() > 128
            || !id
                .bytes()
                .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_')
        {
            return Err(invalid("Ungültige Profil-ID"));
        }
        Ok(self.root.join(format!("{id}.json")))
    }
    pub async fn list(&self) -> Result<ProfileList> {
        if !self.root.exists() {
            return Ok(ProfileList {
                profiles: vec![],
                warnings: vec![],
            });
        }
        let mut entries = fs::read_dir(&self.root).await?;
        let mut list = ProfileList {
            profiles: vec![],
            warnings: vec![],
        };
        while let Some(entry) = entries.next_entry().await? {
            if entry.path().extension().and_then(|s| s.to_str()) != Some("json") {
                continue;
            }
            match Self::read(&entry.path()).await {
                Ok(profile) if self.path(&profile.id).is_ok_and(|p| p == entry.path()) => {
                    list.profiles.push(ProfileSummary {
                        id: profile.id,
                        name: profile.name,
                        description: profile.description,
                        updated_at: profile.updated_at,
                    })
                }
                Ok(_) => list.warnings.push(format!(
                    "{}: Profil-ID passt nicht zum Dateinamen",
                    entry.file_name().to_string_lossy()
                )),
                Err(error) => list
                    .warnings
                    .push(format!("{}: {error}", entry.file_name().to_string_lossy())),
            }
        }
        list.profiles
            .sort_by_key(|profile| profile.name.to_lowercase());
        Ok(list)
    }
    pub async fn load(&self, id: &str) -> Result<CreatorProfile> {
        let profile = Self::read(&self.path(id)?).await?;
        if profile.id != id {
            return Err(invalid("Profil-ID passt nicht zum Dateinamen"));
        }
        Ok(profile)
    }
    async fn read(path: &Path) -> Result<CreatorProfile> {
        let file = fs::File::open(path).await?;
        let mut bytes = Vec::new();
        file.take((MAX_PROFILE_BYTES + 1) as u64)
            .read_to_end(&mut bytes)
            .await?;
        if bytes.len() > MAX_PROFILE_BYTES {
            return Err(invalid("Profil darf höchstens 16 MB groß sein"));
        }
        Self::parse(&bytes)
    }
    fn parse(bytes: &[u8]) -> Result<CreatorProfile> {
        if bytes.len() > MAX_PROFILE_BYTES {
            return Err(invalid("Profil darf höchstens 16 MB groß sein"));
        }
        let mut profile: CreatorProfile = serde_json::from_slice(bytes)?;
        profile.name = checked_name(&profile.name)?;
        if profile.description.chars().count() > 4096 {
            return Err(invalid("Profilbeschreibung ist zu lang"));
        }
        if !profile.settings.is_object() {
            return Err(invalid("Profil-Einstellungen fehlen"));
        }
        normalize_settings(profile.settings.clone())?;
        strip_password(&mut profile.settings);
        Ok(profile)
    }
    async fn save(&self, mut profile: CreatorProfile) -> Result<CreatorProfile> {
        profile.name = checked_name(&profile.name)?;
        profile.description = profile.description.trim().into();
        if profile.description.chars().count() > 4096 {
            return Err(invalid("Profilbeschreibung ist zu lang"));
        }
        normalize_settings(profile.settings.clone())?;
        strip_password(&mut profile.settings);
        profile.updated_at = now();
        let bytes = serde_json::to_vec_pretty(&profile)?;
        if bytes.len() > MAX_PROFILE_BYTES {
            return Err(invalid("Profil darf höchstens 16 MB groß sein"));
        }
        atomic_write(&self.path(&profile.id)?, &bytes).await?;
        Ok(profile)
    }
    pub async fn create(
        &self,
        name: &str,
        description: &str,
        settings: Value,
    ) -> Result<CreatorProfile> {
        self.save(CreatorProfile {
            id: Uuid::new_v4().simple().to_string(),
            name: name.into(),
            description: description.into(),
            created_at: now(),
            updated_at: now(),
            settings,
            extra: Map::new(),
        })
        .await
    }
    pub async fn rename(&self, id: &str, name: &str, description: &str) -> Result<CreatorProfile> {
        let mut profile = self.load(id).await?;
        profile.name = name.into();
        profile.description = description.into();
        self.save(profile).await
    }
    pub async fn import(&self, content: &str) -> Result<CreatorProfile> {
        let mut profile = Self::parse(content.as_bytes())?;
        profile.id = Uuid::new_v4().simple().to_string();
        profile.name.push_str(" (Import)");
        profile.created_at = now();
        self.save(profile).await
    }
    pub async fn import_from(&self, path: &Path) -> Result<CreatorProfile> {
        let profile = Self::read(path).await?;
        self.import(&serde_json::to_string(&profile)?).await
    }
    pub async fn export(&self, id: &str) -> Result<String> {
        Ok(serde_json::to_string_pretty(&self.load(id).await?)?)
    }
    pub async fn export_to(&self, id: &str, path: &Path) -> Result<()> {
        // A profile export cannot replace live settings or one of the stored profiles.
        if !path
            .extension()
            .and_then(|s| s.to_str())
            .is_some_and(|s| s.eq_ignore_ascii_case("ccsprofile"))
        {
            return Err(invalid("Exportdatei muss die Endung .ccsprofile haben"));
        }
        atomic_write(path, self.export(id).await?.as_bytes()).await
    }
    pub async fn delete(&self, id: &str) -> Result<()> {
        match fs::remove_file(self.path(id)?).await {
            Ok(()) => Ok(()),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(e) => Err(e.into()),
        }
    }
    pub async fn prepare_apply(&self, id: &str, current: &Value) -> Result<Value> {
        self.prepare_named_apply(id, current)
            .await
            .map(|(_, settings)| settings)
    }
    pub async fn prepare_named_apply(&self, id: &str, current: &Value) -> Result<(String, Value)> {
        let profile = self.load(id).await?;
        let mut settings = normalize_settings(profile.settings)?;
        if let Some(password) = current.pointer("/StreamerBot/Password") {
            settings["StreamerBot"]["Password"] = password.clone();
        }
        Ok((profile.name, settings))
    }
}
fn strip_password(settings: &mut Value) {
    if !settings["StreamerBot"].is_object() {
        settings["StreamerBot"] = Value::Object(Map::new());
    }
    settings["StreamerBot"]["Password"] = Value::String(String::new());
}
fn checked_name(name: &str) -> Result<String> {
    let name = name.trim();
    if name.is_empty() || name.chars().count() > 160 {
        return Err(invalid("Profilname muss 1–160 Zeichen enthalten"));
    }
    Ok(name.into())
}
fn now() -> String {
    Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis, true)
}
fn invalid(message: &str) -> SettingsError {
    SettingsError::Validation(message.into())
}
async fn atomic_write(path: &Path, bytes: &[u8]) -> Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).await?;
    }
    let temp = path.with_extension(format!("{}.tmp", Uuid::new_v4().simple()));
    let result = async {
        let mut file = fs::File::create(&temp).await?;
        file.write_all(bytes).await?;
        file.flush().await?;
        file.sync_all().await?;
        drop(file);
        fs::rename(&temp, path).await?;
        Ok::<_, SettingsError>(())
    }
    .await;
    if result.is_err() {
        let _ = fs::remove_file(&temp).await;
    }
    result
}
