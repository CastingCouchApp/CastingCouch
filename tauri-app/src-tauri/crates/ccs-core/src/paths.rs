use std::path::PathBuf;

/// Application data folder name, matching the WPF app under LocalAppData.
pub const APP_FOLDER: &str = "CreatorControlSuite";
pub const SETTINGS_FILE: &str = "settings.json";
pub const SINGLE_INSTANCE_LOCK: &str = "castingcouch.lock";

#[derive(Debug, Clone)]
pub struct AppPaths {
    pub data_root: PathBuf,
    pub settings_file: PathBuf,
    pub overlay_root: PathBuf,
    pub overlay_layouts: PathBuf,
    pub logs: PathBuf,
    pub crash_reports: PathBuf,
    pub lock_file: PathBuf,
}

impl AppPaths {
    pub fn from_os() -> Result<Self, PathError> {
        let data_root = dirs::data_local_dir()
            .ok_or(PathError::NoDataDir)?
            .join(APP_FOLDER);
        Ok(Self::from_root(data_root))
    }

    pub fn from_root(data_root: PathBuf) -> Self {
        Self {
            settings_file: data_root.join(SETTINGS_FILE),
            overlay_root: data_root.join("Overlay"),
            overlay_layouts: data_root.join("Overlay").join("layouts"),
            logs: data_root.join("Logs"),
            crash_reports: data_root.join("CrashReports"),
            lock_file: data_root.join(SINGLE_INSTANCE_LOCK),
            data_root,
        }
    }

    pub fn ensure_dirs(&self) -> Result<(), PathError> {
        for dir in [
            &self.data_root,
            &self.overlay_root,
            &self.overlay_layouts,
            &self.logs,
            &self.crash_reports,
        ] {
            std::fs::create_dir_all(dir).map_err(PathError::Io)?;
        }
        Ok(())
    }
}

#[derive(Debug, thiserror::Error)]
pub enum PathError {
    #[error("could not resolve local application data directory")]
    NoDataDir,
    #[error("io error: {0}")]
    Io(#[from] std::io::Error),
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn from_root_uses_wpf_layout() {
        let dir = tempdir().unwrap();
        let paths = AppPaths::from_root(dir.path().join(APP_FOLDER));
        assert_eq!(paths.settings_file.file_name().unwrap(), SETTINGS_FILE);
        assert!(paths.overlay_layouts.ends_with("Overlay/layouts") || paths.overlay_layouts.ends_with(r"Overlay\layouts"));
    }
}
