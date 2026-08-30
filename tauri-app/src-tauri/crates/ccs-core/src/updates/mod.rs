use sha2::{Digest, Sha256};
use std::path::{Path, PathBuf};

pub const PRODUCT_ID: &str = "CreatorControlSuite";
pub const DEFAULT_GITHUB_OWNER: &str = "CastingCouchApp";
pub const DEFAULT_GITHUB_REPO: &str = "CastingCouch";

#[derive(Debug, thiserror::Error)]
pub enum UpdateError {
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    #[error("json: {0}")]
    Json(#[from] serde_json::Error),
    #[error("sha256 mismatch")]
    ChecksumMismatch,
    #[error("invalid manifest")]
    InvalidManifest,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct UpdateManifest {
    pub product_id: String,
    pub version: String,
    pub channel: String,
    pub package_file_name: String,
    #[serde(alias = "PackageSha256", alias = "packageSha256")]
    pub sha256: String,
    #[serde(
        rename = "Size",
        alias = "PackageSizeBytes",
        alias = "packageSizeBytes"
    )]
    pub size: u64,
    #[serde(default)]
    pub published_at: String,
    #[serde(default)]
    pub minimum_version: String,
    #[serde(default)]
    pub release_notes: String,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct GitHubAsset {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub browser_download_url: String,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct GitHubRelease {
    #[serde(default)]
    pub tag_name: String,
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub draft: bool,
    #[serde(default)]
    pub prerelease: bool,
    #[serde(default)]
    pub body: String,
    #[serde(default)]
    pub assets: Vec<GitHubAsset>,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct UpdatePackage {
    pub product_id: String,
    pub version: String,
    pub channel: String,
    pub download_uri: String,
    pub sha256: String,
    pub size: u64,
    pub release_notes: String,
    pub package_file_name: String,
}

impl UpdatePackage {
    pub fn to_manifest(&self) -> UpdateManifest {
        UpdateManifest {
            product_id: self.product_id.clone(),
            version: self.version.clone(),
            channel: self.channel.clone(),
            package_file_name: self.package_file_name.clone(),
            sha256: self.sha256.clone(),
            size: self.size,
            published_at: String::new(),
            minimum_version: String::new(),
            release_notes: self.release_notes.clone(),
        }
    }
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct UpdateCheckResult {
    pub update_available: bool,
    pub current_version: String,
    pub package: Option<UpdatePackage>,
    pub detail: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ProductVersion {
    pub major: u32,
    pub minor: u32,
    pub patch: u32,
    pub pre_label: Option<String>,
    pub pre_number: u32,
}

impl ProductVersion {
    pub fn is_prerelease(&self) -> bool {
        self.pre_label.is_some()
    }
}

impl PartialOrd for ProductVersion {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for ProductVersion {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        self.major
            .cmp(&other.major)
            .then(self.minor.cmp(&other.minor))
            .then(self.patch.cmp(&other.patch))
            .then_with(|| match (self.is_prerelease(), other.is_prerelease()) {
                (false, false) => std::cmp::Ordering::Equal,
                (false, true) => std::cmp::Ordering::Greater,
                (true, false) => std::cmp::Ordering::Less,
                (true, true) => pre_release_rank(self.pre_label.as_deref())
                    .cmp(&pre_release_rank(other.pre_label.as_deref()))
                    .then(self.pre_number.cmp(&other.pre_number)),
            })
    }
}

fn pre_release_rank(label: Option<&str>) -> u8 {
    match label {
        Some("alpha") => 1,
        Some("beta") => 2,
        Some("rc") => 3,
        _ => 0,
    }
}

pub fn github_releases_url(owner: &str, repo: &str) -> String {
    format!("https://api.github.com/repos/{owner}/{repo}/releases?per_page=30")
}

pub fn parse_manifest_bytes(bytes: &[u8]) -> Result<UpdateManifest, UpdateError> {
    Ok(serde_json::from_slice(bytes)?)
}

pub fn parse_releases_bytes(bytes: &[u8]) -> Result<Vec<GitHubRelease>, UpdateError> {
    Ok(serde_json::from_slice(bytes)?)
}

pub fn sha256_hex(bytes: &[u8]) -> String {
    let mut hasher = Sha256::new();
    hasher.update(bytes);
    format!("{:x}", hasher.finalize())
}

pub fn verify_package(manifest: &UpdateManifest, package_bytes: &[u8]) -> Result<(), UpdateError> {
    if manifest.product_id.is_empty() || manifest.version.is_empty() {
        return Err(UpdateError::InvalidManifest);
    }
    let actual = sha256_hex(package_bytes);
    if !actual.eq_ignore_ascii_case(&manifest.sha256) {
        return Err(UpdateError::ChecksumMismatch);
    }
    if manifest.size > 0 && manifest.size != package_bytes.len() as u64 {
        return Err(UpdateError::ChecksumMismatch);
    }
    Ok(())
}

pub fn load_manifest(path: &Path) -> Result<UpdateManifest, UpdateError> {
    let bytes = std::fs::read(path)?;
    parse_manifest_bytes(&bytes)
}

/// Writes `package_bytes` to `dest` only if SHA-256 and size match. Deletes the file on failure.
pub fn store_verified_package(
    dest: &Path,
    manifest: &UpdateManifest,
    package_bytes: &[u8],
) -> Result<PathBuf, UpdateError> {
    if let Some(parent) = dest.parent() {
        std::fs::create_dir_all(parent)?;
    }
    std::fs::write(dest, package_bytes)?;
    match verify_package(manifest, package_bytes) {
        Ok(()) => Ok(dest.to_path_buf()),
        Err(error) => {
            let _ = std::fs::remove_file(dest);
            Err(error)
        }
    }
}

pub fn parse_product_version(value: &str) -> Option<ProductVersion> {
    let mut trimmed = value.trim();
    if let Some(rest) = trimmed
        .strip_prefix('v')
        .or_else(|| trimmed.strip_prefix('V'))
    {
        trimmed = rest;
    }
    if let Some((core, _)) = trimmed.split_once('+') {
        trimmed = core;
    }
    let (core, pre) = match trimmed.split_once('-') {
        Some((core, pre)) => (core, Some(pre.replace('.', ""))),
        None => (trimmed, None),
    };
    let mut parts = core.split('.');
    let major = parts.next()?.parse().ok()?;
    let minor = parts.next()?.parse().ok()?;
    let patch = parts.next()?.parse().ok()?;
    if parts.next().is_some() {
        return None;
    }
    let (pre_label, pre_number) = match pre {
        None => (None, 0),
        Some(pre) => {
            let pre = pre.trim();
            if pre.is_empty() {
                return None;
            }
            let split = pre.find(|c: char| c.is_ascii_digit()).unwrap_or(pre.len());
            let label = pre[..split].to_ascii_lowercase();
            if label.is_empty() || !label.chars().all(|c| c.is_ascii_alphabetic()) {
                return None;
            }
            let pre_number = if split < pre.len() {
                pre[split..].parse().ok()?
            } else {
                0
            };
            (Some(label), pre_number)
        }
    };
    Some(ProductVersion {
        major,
        minor,
        patch,
        pre_label,
        pre_number,
    })
}

pub fn normalize_channel(channel: &str) -> String {
    match channel.trim().to_ascii_lowercase().as_str() {
        "stable" => "Stable".into(),
        "beta" => "Beta".into(),
        _ => "Alpha".into(),
    }
}

pub fn manifest_asset_url(release: &GitHubRelease) -> Option<&str> {
    find_asset(release, "update-manifest.json")
        .map(|asset| asset.browser_download_url.as_str())
        .filter(|url| !url.trim().is_empty())
}

pub fn select_release<'a>(
    releases: &'a [GitHubRelease],
    channel: &str,
) -> Option<&'a GitHubRelease> {
    let normalized = normalize_channel(channel);
    releases
        .iter()
        .filter(|release| !release.draft)
        .find(|release| matches_channel(release, &normalized))
}

fn matches_channel(release: &GitHubRelease, channel: &str) -> bool {
    let haystack = format!("{} {}", release.tag_name, release.name);
    match channel {
        "Stable" => !release.prerelease,
        "Beta" => {
            release.prerelease
                && (contains_ignore_ascii(&haystack, "beta")
                    || !contains_ignore_ascii(&haystack, "alpha"))
        }
        _ => true,
    }
}

fn contains_ignore_ascii(haystack: &str, needle: &str) -> bool {
    haystack
        .to_ascii_lowercase()
        .contains(&needle.to_ascii_lowercase())
}

fn find_asset<'a>(release: &'a GitHubRelease, name: &str) -> Option<&'a GitHubAsset> {
    release
        .assets
        .iter()
        .find(|asset| asset.name.eq_ignore_ascii_case(name))
}

pub fn evaluate_check(
    current_version: &str,
    channel: &str,
    releases: &[GitHubRelease],
    manifest: &UpdateManifest,
) -> UpdateCheckResult {
    let current_version = current_version.to_string();
    let Some(release) = select_release(releases, channel) else {
        return UpdateCheckResult {
            update_available: false,
            current_version,
            package: None,
            detail: format!(
                "Kein GitHub-Release für Kanal {} gefunden.",
                normalize_channel(channel)
            ),
        };
    };

    if manifest.product_id != PRODUCT_ID {
        return UpdateCheckResult {
            update_available: false,
            current_version,
            package: None,
            detail: "Update-Manifest hat eine ungültige ProductId.".into(),
        };
    }

    let Some(package_asset) = find_asset(release, &manifest.package_file_name) else {
        return UpdateCheckResult {
            update_available: false,
            current_version,
            package: None,
            detail: format!("Paket {} fehlt im Release.", manifest.package_file_name),
        };
    };
    if package_asset.browser_download_url.trim().is_empty() {
        return UpdateCheckResult {
            update_available: false,
            current_version,
            package: None,
            detail: format!("Paket {} fehlt im Release.", manifest.package_file_name),
        };
    }

    let Some(current) = parse_product_version(&current_version) else {
        return UpdateCheckResult {
            update_available: false,
            current_version,
            package: None,
            detail: "Versionsvergleich fehlgeschlagen.".into(),
        };
    };
    let Some(candidate) = parse_product_version(&manifest.version) else {
        return UpdateCheckResult {
            update_available: false,
            current_version,
            package: None,
            detail: "Versionsvergleich fehlgeschlagen.".into(),
        };
    };

    if candidate <= current {
        return UpdateCheckResult {
            update_available: false,
            current_version: current_version.clone(),
            package: None,
            detail: format!(
                "Aktuelle Version {current_version} ist aktuell ({}).",
                normalize_channel(channel)
            ),
        };
    }

    if !manifest.minimum_version.trim().is_empty() {
        if let Some(minimum) = parse_product_version(&manifest.minimum_version) {
            if current < minimum {
                return UpdateCheckResult {
                    update_available: false,
                    current_version,
                    package: None,
                    detail: format!(
                        "Update {} erfordert mindestens Version {}.",
                        manifest.version, manifest.minimum_version
                    ),
                };
            }
        }
    }

    let release_notes = if manifest.release_notes.trim().is_empty() {
        release.body.clone()
    } else {
        manifest.release_notes.clone()
    };

    UpdateCheckResult {
        update_available: true,
        package: Some(UpdatePackage {
            product_id: manifest.product_id.clone(),
            version: manifest.version.clone(),
            channel: manifest.channel.clone(),
            download_uri: package_asset.browser_download_url.clone(),
            sha256: manifest.sha256.clone(),
            size: manifest.size,
            release_notes,
            package_file_name: manifest.package_file_name.clone(),
        }),
        detail: format!("Update {} verfügbar.", manifest.version),
        current_version,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const MANIFEST_FIXTURE: &str = include_str!("fixtures/update-manifest.json");
    const RELEASES_FIXTURE: &str = include_str!("fixtures/github-releases.json");

    fn sample_manifest(sha: &str, size: u64) -> UpdateManifest {
        UpdateManifest {
            product_id: PRODUCT_ID.into(),
            version: "8.0.0-beta1".into(),
            channel: "Beta".into(),
            package_file_name: "app.zip".into(),
            sha256: sha.into(),
            size,
            published_at: String::new(),
            minimum_version: String::new(),
            release_notes: String::new(),
        }
    }

    fn fixture_manifest() -> UpdateManifest {
        parse_manifest_bytes(MANIFEST_FIXTURE.as_bytes()).expect("fixture manifest")
    }

    fn fixture_releases() -> Vec<GitHubRelease> {
        parse_releases_bytes(RELEASES_FIXTURE.as_bytes()).expect("fixture releases")
    }

    #[test]
    fn parses_wpf_manifest_aliases() {
        let manifest = fixture_manifest();
        assert_eq!(manifest.product_id, PRODUCT_ID);
        assert_eq!(manifest.version, "8.0.0-beta2");
        assert_eq!(
            manifest.package_file_name,
            "CreatorControlSuite-8.0.0-beta2-win-x64.zip"
        );
        assert_eq!(manifest.sha256, "DEADBEEF");
        assert_eq!(manifest.size, 9);
        assert_eq!(manifest.release_notes, "Phase 4.4 fixture notes");
    }

    #[test]
    fn verifies_matching_hash() {
        let bytes = b"zip-bytes";
        let hash = sha256_hex(bytes);
        let manifest = sample_manifest(&hash, bytes.len() as u64);
        assert!(verify_package(&manifest, bytes).is_ok());
    }

    #[test]
    fn rejects_bad_hash() {
        let manifest = sample_manifest("deadbeef", 1);
        assert!(matches!(
            verify_package(&manifest, b"x"),
            Err(UpdateError::ChecksumMismatch)
        ));
        assert_eq!(UpdateError::ChecksumMismatch.to_string(), "sha256 mismatch");
    }

    #[test]
    fn rejects_size_mismatch() {
        let bytes = b"zip-bytes";
        let hash = sha256_hex(bytes);
        let manifest = sample_manifest(&hash, 99);
        assert!(matches!(
            verify_package(&manifest, bytes),
            Err(UpdateError::ChecksumMismatch)
        ));
    }

    #[test]
    fn fixture_checksum_is_rejected_for_dummy_bytes() {
        let manifest = fixture_manifest();
        assert!(matches!(
            verify_package(&manifest, b"zip-bytes"),
            Err(UpdateError::ChecksumMismatch)
        ));
    }

    #[test]
    fn store_verified_package_keeps_matching_file() {
        let dir = tempfile::tempdir().unwrap();
        let dest = dir.path().join("Downloads").join("pkg.zip");
        let bytes = b"zip-bytes";
        let hash = sha256_hex(bytes);
        let manifest = sample_manifest(&hash, bytes.len() as u64);
        store_verified_package(&dest, &manifest, bytes).unwrap();
        assert_eq!(std::fs::read(&dest).unwrap(), bytes);
    }

    #[test]
    fn store_verified_package_rejects_bad_hash_and_deletes_file() {
        let dir = tempfile::tempdir().unwrap();
        let dest = dir.path().join("Downloads").join("pkg.zip");
        let manifest = sample_manifest("deadbeef", 1);
        let err = store_verified_package(&dest, &manifest, b"x").unwrap_err();
        assert!(matches!(err, UpdateError::ChecksumMismatch));
        assert!(!dest.exists());
        assert!(dest.parent().unwrap().exists());
    }

    #[test]
    fn parses_tauri_and_wpf_prerelease_as_equal() {
        let tauri = parse_product_version("8.0.0-beta.1").unwrap();
        let wpf = parse_product_version("8.0.0-beta1").unwrap();
        assert_eq!(tauri, wpf);
        assert!(parse_product_version("v8.0.0-beta2").unwrap() > tauri);
        assert!(parse_product_version("8.0.0").unwrap() > wpf);
    }

    #[test]
    fn evaluate_check_reports_available_update() {
        let releases = fixture_releases();
        let mut manifest = fixture_manifest();
        manifest.sha256 = "abc".into();
        let result = evaluate_check("8.0.0-beta.1", "Beta", &releases, &manifest);
        assert!(result.update_available);
        let package = result.package.expect("package");
        assert_eq!(package.version, "8.0.0-beta2");
        assert_eq!(package.download_uri, "https://example.test/pkg.zip");
        assert_eq!(package.release_notes, "Phase 4.4 fixture notes");
        assert_eq!(package.sha256, "abc");
    }

    #[test]
    fn evaluate_check_uses_release_body_when_notes_empty() {
        let releases = fixture_releases();
        let mut manifest = fixture_manifest();
        manifest.release_notes.clear();
        let result = evaluate_check("8.0.0-beta.1", "Beta", &releases, &manifest);
        assert_eq!(result.package.unwrap().release_notes, "from-body");
    }

    #[test]
    fn evaluate_check_rejects_current_or_older() {
        let releases = fixture_releases();
        let manifest = fixture_manifest();
        let result = evaluate_check("8.0.0-beta2", "Beta", &releases, &manifest);
        assert!(!result.update_available);
        assert!(result.detail.contains("ist aktuell"));
    }

    #[test]
    fn evaluate_check_reports_missing_channel() {
        let result = evaluate_check("8.0.0", "Stable", &[], &fixture_manifest());
        assert!(!result.update_available);
        assert!(result.detail.contains("Kein GitHub-Release"));
    }

    #[test]
    fn github_releases_url_uses_castingcouch_repo() {
        assert_eq!(
            github_releases_url(DEFAULT_GITHUB_OWNER, DEFAULT_GITHUB_REPO),
            "https://api.github.com/repos/CastingCouchApp/CastingCouch/releases?per_page=30"
        );
    }
}
