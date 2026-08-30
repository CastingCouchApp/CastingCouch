pub mod instance;
pub mod logging;
pub mod paths;
pub mod settings;
pub mod store;
pub mod updates;

pub use instance::{InstanceError, SingleInstanceLock};
pub use paths::{AppPaths, PathError};
pub use settings::{
    AlertDefinitionSettings, AlertSettings, AppSettings, OverlayCanvasSettings, OverlaySettings,
    SidecarSettings, CURRENT_SCHEMA_VERSION,
};
pub use store::{migrate, JsonSettingsStore, SettingsError};
pub use updates::{
    apply_verified_update, current_tauri_manifest_asset_name, evaluate_check,
    evaluate_signed_check, github_releases_url, launch_installer, manifest_asset_name,
    manifest_asset_url, parse_manifest_bytes, parse_releases_bytes, select_release, sha256_hex,
    store_verified_package, tauri_manifest_asset_url, verify_manifest_signature, verify_package,
    GitHubRelease, UpdateCheckResult, UpdateError, UpdateManifest, UpdatePackage, UpdateStack,
    DEFAULT_GITHUB_OWNER, DEFAULT_GITHUB_REPO, MANIFEST_TAURI_MACOS, MANIFEST_TAURI_WIN,
    MANIFEST_WPF, PRODUCT_ID,
};
