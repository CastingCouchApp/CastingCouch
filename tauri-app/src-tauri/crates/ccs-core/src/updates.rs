use sha2::{Digest, Sha256};
use std::path::Path;

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
    pub sha256: String,
    pub size: u64,
    #[serde(default)]
    pub published_at: String,
    #[serde(default)]
    pub minimum_version: String,
    #[serde(default)]
    pub release_notes: String,
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
    Ok(serde_json::from_slice(&bytes)?)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn verifies_matching_hash() {
        let bytes = b"zip-bytes";
        let hash = sha256_hex(bytes);
        let manifest = UpdateManifest {
            product_id: "CreatorControlSuite".into(),
            version: "8.0.0-beta1".into(),
            channel: "Beta".into(),
            package_file_name: "app.zip".into(),
            sha256: hash,
            size: bytes.len() as u64,
            published_at: String::new(),
            minimum_version: String::new(),
            release_notes: String::new(),
        };
        assert!(verify_package(&manifest, bytes).is_ok());
    }

    #[test]
    fn rejects_bad_hash() {
        let manifest = UpdateManifest {
            product_id: "CreatorControlSuite".into(),
            version: "8.0.0-beta1".into(),
            channel: "Beta".into(),
            package_file_name: "app.zip".into(),
            sha256: "deadbeef".into(),
            size: 1,
            published_at: String::new(),
            minimum_version: String::new(),
            release_notes: String::new(),
        };
        assert!(matches!(
            verify_package(&manifest, b"x"),
            Err(UpdateError::ChecksumMismatch)
        ));
    }
}
