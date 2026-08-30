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
    evaluate_check, github_releases_url, manifest_asset_url, parse_manifest_bytes,
    parse_releases_bytes, select_release, sha256_hex, store_verified_package, verify_package,
    GitHubRelease, UpdateCheckResult, UpdateError, UpdateManifest, UpdatePackage,
    DEFAULT_GITHUB_OWNER, DEFAULT_GITHUB_REPO, PRODUCT_ID,
};
