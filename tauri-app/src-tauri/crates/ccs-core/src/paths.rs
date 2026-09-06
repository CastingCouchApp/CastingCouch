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
    pub backups: PathBuf,
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
            backups: data_root.join("Backups"),
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
            &self.backups,
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
        assert!(
            paths.overlay_layouts.ends_with("Overlay/layouts")
                || paths.overlay_layouts.ends_with(r"Overlay\layouts")
        );
        assert!(paths.backups.ends_with("Backups") || paths.backups.ends_with(r"Backups"));
    }
}

#[cfg(test)]
mod overlay_path_tests {
    use super::*;
    #[test]
    fn configured_data_path_and_legacy_nested_root_are_respected() {
        let dir = tempfile::tempdir().unwrap();
        let paths = AppPaths::from_root(dir.path().into());
        let mut settings = crate::AppSettings::default();
        assert_eq!(
            overlay_data_path(&paths, &settings),
            paths.overlay_root.join("data/overlay-data.json")
        );
        settings.overlay.data_file_path = dir.path().join("custom.json").to_string_lossy().into();
        assert_eq!(
            overlay_data_path(&paths, &settings),
            dir.path().join("custom.json")
        );
        settings.overlay.data_file_path.clear();
        settings.overlay.root_path = dir.path().join("imported").to_string_lossy().into();
        std::fs::create_dir_all(dir.path().join("imported/Overlay/data")).unwrap();
        std::fs::write(
            dir.path().join("imported/Overlay/data/overlay-data.json"),
            "{}",
        )
        .unwrap();
        assert_eq!(
            overlay_data_path(&paths, &settings),
            dir.path().join("imported/Overlay/data/overlay-data.json")
        );
    }
}

pub fn expand_path(value: &str) -> PathBuf {
    let mut result = value.to_string();
    for (key, value) in std::env::vars() {
        result = result.replace(&format!("%{key}%"), &value);
    }
    if let Some(rest) = result.strip_prefix("~/") {
        if let Some(home) = dirs::home_dir() {
            return home.join(rest);
        }
    }
    PathBuf::from(result)
}
pub fn overlay_data_path(paths: &AppPaths, settings: &crate::AppSettings) -> PathBuf {
    let root = if settings.overlay.root_path.trim().is_empty() {
        paths.overlay_root.clone()
    } else {
        expand_path(settings.overlay.root_path.trim())
    };
    let nested = root
        .join("Overlay/data")
        .join(&settings.overlay.data_file_name);
    if root.join("Overlay/modules/ui").exists()
        && (root.join("Overlay/modules/ui/spotify.html").exists()
            || root.join("Overlay/modules/ui/live-status.html").exists())
    {
        return root.join("Overlay/data/overlay-data.json");
    }
    if !settings.overlay.data_file_path.trim().is_empty() {
        return expand_path(settings.overlay.data_file_path.trim());
    }
    if nested.exists() {
        return nested;
    }
    let prior = paths
        .data_root
        .join("data")
        .join(&settings.overlay.data_file_name);
    if settings.overlay.root_path.trim().is_empty() && prior.exists() {
        return prior;
    }
    root.join("data").join(&settings.overlay.data_file_name)
}
