use super::UpdateError;
use std::path::{Path, PathBuf};

/// Backup current install, then invoke `launch` with the verified package path.
/// `launch` is injectable so unit tests never spawn a real installer.
pub fn apply_verified_update<F>(
    package_path: &Path,
    install_dir: &Path,
    backup_root: &Path,
    current_version: &str,
    launch: F,
) -> Result<PathBuf, UpdateError>
where
    F: FnOnce(&Path) -> Result<(), UpdateError>,
{
    if !package_path.is_file() {
        return Err(UpdateError::Apply("Updatepaket fehlt.".into()));
    }
    let backup_dest = backup_root.join(sanitize_version(current_version));
    if install_dir.exists() {
        backup_install_dir(install_dir, &backup_dest)?;
    }
    launch(package_path)?;
    Ok(backup_dest)
}

pub fn backup_install_dir(install_dir: &Path, backup_dest: &Path) -> Result<(), UpdateError> {
    if backup_dest.exists() {
        std::fs::remove_dir_all(backup_dest)?;
    }
    copy_dir_all(install_dir, backup_dest)?;
    Ok(())
}

pub fn installer_launch_command(package_path: &Path) -> Result<(String, Vec<String>), UpdateError> {
    let ext = package_path
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    let path = package_path.to_string_lossy().into_owned();
    match ext.as_str() {
        "msi" => Ok(("msiexec".into(), vec!["/i".into(), path])),
        "dmg" | "app" => Ok(("open".into(), vec![path])),
        "exe" => Ok((path, vec![])),
        _ => Err(UpdateError::Apply(format!(
            "Unbekanntes Update-Paketformat: {}",
            package_path.display()
        ))),
    }
}

pub fn launch_installer(package_path: &Path) -> Result<(), UpdateError> {
    let (program, args) = installer_launch_command(package_path)?;
    std::process::Command::new(&program)
        .args(&args)
        .spawn()
        .map_err(|e| UpdateError::Apply(e.to_string()))?;
    Ok(())
}

fn sanitize_version(version: &str) -> String {
    version
        .chars()
        .map(|c| if c.is_ascii_alphanumeric() || c == '.' || c == '-' { c } else { '_' })
        .collect()
}

fn copy_dir_all(src: &Path, dst: &Path) -> Result<(), UpdateError> {
    std::fs::create_dir_all(dst)?;
    for entry in std::fs::read_dir(src)? {
        let entry = entry?;
        let dest = dst.join(entry.file_name());
        if entry.file_type()?.is_dir() {
            copy_dir_all(&entry.path(), &dest)?;
        } else {
            std::fs::copy(entry.path(), &dest)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicBool, Ordering};

    #[test]
    fn installer_command_uses_nsis_msi_and_dmg() {
        let exe = Path::new("CastingCouch-8.0.0-beta2-win-x64-setup.exe");
        let (program, args) = installer_launch_command(exe).unwrap();
        assert!(program.ends_with("CastingCouch-8.0.0-beta2-win-x64-setup.exe"));
        assert!(args.is_empty());

        let msi = Path::new("CastingCouch-8.0.0-beta2-win-x64.msi");
        let (program, args) = installer_launch_command(msi).unwrap();
        assert_eq!(program, "msiexec");
        assert_eq!(args, vec!["/i".to_string(), msi.to_string_lossy().into_owned()]);

        let dmg = Path::new("CastingCouch-8.0.0-beta2-macos.dmg");
        let (program, args) = installer_launch_command(dmg).unwrap();
        assert_eq!(program, "open");
        assert_eq!(args, vec![dmg.to_string_lossy().into_owned()]);
    }

    #[test]
    fn installer_command_rejects_unknown_extension() {
        let err = installer_launch_command(Path::new("notes.txt")).unwrap_err();
        assert!(matches!(err, UpdateError::Apply(_)));
    }

    #[test]
    fn apply_copies_install_dir_then_calls_launch() {
        let root = tempfile::tempdir().unwrap();
        let install = root.path().join("app");
        std::fs::create_dir_all(&install).unwrap();
        std::fs::write(install.join("CastingCouch.exe"), b"bin").unwrap();
        let package = root.path().join("CastingCouch-8.0.0-beta2-win-x64-setup.exe");
        std::fs::write(&package, b"setup").unwrap();
        let backup_root = root.path().join("Backups");
        let launched = AtomicBool::new(false);

        let dest = apply_verified_update(
            &package,
            &install,
            &backup_root,
            "8.0.0-beta.1",
            |path| {
                launched.store(true, Ordering::SeqCst);
                assert_eq!(path, package.as_path());
                Ok(())
            },
        )
        .unwrap();

        assert!(launched.load(Ordering::SeqCst));
        assert_eq!(dest, backup_root.join("8.0.0-beta.1"));
        assert_eq!(
            std::fs::read(dest.join("CastingCouch.exe")).unwrap(),
            b"bin"
        );
    }

    #[test]
    fn apply_rejects_missing_package_without_launching() {
        let root = tempfile::tempdir().unwrap();
        let launched = AtomicBool::new(false);
        let err = apply_verified_update(
            &root.path().join("missing.exe"),
            &root.path().join("app"),
            &root.path().join("Backups"),
            "8.0.0",
            |_| {
                launched.store(true, Ordering::SeqCst);
                Ok(())
            },
        )
        .unwrap_err();
        assert!(matches!(err, UpdateError::Apply(_)));
        assert!(!launched.load(Ordering::SeqCst));
    }
}
