//! Launch configured desktop applications without a command shell.
use crate::AppSettings;
use serde::Serialize;
use std::{
    ffi::OsString,
    path::PathBuf,
    process::{Command, Stdio},
    sync::Mutex,
};
use sysinfo::{ProcessRefreshKind, RefreshKind, System, UpdateKind};

static LAUNCH_GATE: Mutex<()> = Mutex::new(());

#[derive(Debug, Clone, Copy, PartialEq)]
enum Platform {
    Windows,
    Mac,
    Other,
}
impl Platform {
    fn current() -> Self {
        if cfg!(windows) {
            Self::Windows
        } else if cfg!(target_os = "macos") {
            Self::Mac
        } else {
            Self::Other
        }
    }
}
#[derive(Debug)]
struct LaunchPlan {
    service: String,
    display_name: &'static str,
    target: PathBuf,
    program: PathBuf,
    args: Vec<OsString>,
    working_directory: PathBuf,
    bundle: bool,
}
impl LaunchPlan {
    fn prepare(settings: &AppSettings, service: &str, platform: Platform) -> Result<Self, String> {
        let (display_name, configured) = match service {
            "obs" => ("OBS", settings.obs.executable_path.as_str()),
            "spotify" => (
                "Spotify",
                settings.spotify.extra["ExecutablePath"]
                    .as_str()
                    .unwrap_or(""),
            ),
            _ => return Err("Dieser Dienst besitzt keinen unterstützten Programmstart.".into()),
        };
        if configured.trim().is_empty() {
            return Err(format!(
                "Bitte zuerst unter Einstellungen den Programmpfad für {display_name} hinterlegen."
            ));
        }
        let path = PathBuf::from(configured.trim());
        if !path.is_absolute() {
            return Err("Der Programmpfad muss absolut sein.".into());
        }
        let target = std::fs::canonicalize(&path)
            .map_err(|e| format!("Programmpfad für {display_name} ist nicht verfügbar: {e}"))?;
        let bundle = platform == Platform::Mac
            && target.is_dir()
            && target
                .extension()
                .is_some_and(|v| v.eq_ignore_ascii_case("app"));
        if bundle {
            if !target.join("Contents/Info.plist").is_file() {
                return Err("Das ausgewählte App-Bundle enthält keine Info.plist.".into());
            }
        } else if !target.is_file()
            || (platform == Platform::Windows
                && !target
                    .extension()
                    .is_some_and(|v| v.eq_ignore_ascii_case("exe")))
        {
            return Err(format!(
                "Programmpfad für {display_name}: ausführbare Datei{} erwartet.",
                if platform == Platform::Mac {
                    " oder .app-Bundle"
                } else {
                    ""
                }
            ));
        }
        let working_directory = target
            .parent()
            .ok_or("Programmverzeichnis fehlt")?
            .to_path_buf();
        let (program, args) = if bundle {
            (
                PathBuf::from("/usr/bin/open"),
                vec![OsString::from("-a"), target.clone().into_os_string()],
            )
        } else {
            (target.clone(), vec![])
        };
        Ok(Self {
            service: service.into(),
            display_name,
            target,
            program,
            args,
            working_directory,
            bundle,
        })
    }
    fn is_running(&self) -> bool {
        // Read only names/executable paths; do not collect command lines or environments.
        let system = System::new_with_specifics(
            RefreshKind::nothing()
                .with_processes(ProcessRefreshKind::nothing().with_exe(UpdateKind::OnlyIfNotSet)),
        );
        system.processes().values().any(|process| {
            if self.bundle {
                return process
                    .exe()
                    .is_some_and(|exe| exe.starts_with(&self.target));
            }
            let wanted = self
                .target
                .file_stem()
                .unwrap_or_default()
                .to_string_lossy();
            let actual = std::path::Path::new(process.name())
                .file_stem()
                .unwrap_or_default()
                .to_string_lossy();
            // Match C# GetProcessesByName: an already running configured program is not started twice.
            actual.eq_ignore_ascii_case(&wanted)
        })
    }
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LaunchResult {
    pub service: String,
    pub status: String,
    pub message: String,
}

pub fn launch_configured_service(
    settings: &AppSettings,
    service: &str,
) -> Result<LaunchResult, String> {
    let _guard = LAUNCH_GATE
        .lock()
        .map_err(|_| "Programmstart-Sperre nicht verfügbar")?;
    let plan = LaunchPlan::prepare(settings, service, Platform::current())?;
    if plan.is_running() {
        return Ok(LaunchResult {
            service: plan.service,
            status: "already_running".into(),
            message: format!("{} läuft bereits.", plan.display_name),
        });
    }
    let mut child = Command::new(&plan.program)
        .args(&plan.args)
        .current_dir(&plan.working_directory)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|e| format!("{} konnte nicht gestartet werden: {e}", plan.display_name))?;
    if plan.bundle {
        let status = child.wait().map_err(|e| e.to_string())?;
        if !status.success() {
            return Err(format!(
                "{} konnte nicht geöffnet werden (open: {status}).",
                plan.display_name
            ));
        }
    } else {
        // Reap the child when it exits, while keeping the app's lifetime independent of ours.
        std::thread::spawn(move || {
            let _ = child.wait();
        });
    }
    Ok(LaunchResult {
        service: plan.service,
        status: "started".into(),
        message: format!(
            "Programmstart für {} angefordert. Der Verbindungsstatus wird separat geprüft.",
            plan.display_name
        ),
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn configured_paths_and_mac_bundles_keep_spaces_and_reject_removed_services() {
        let root = tempfile::tempdir().unwrap();
        let exe = root.path().join("OBS Studio.exe");
        std::fs::write(&exe, []).unwrap();
        let bundle = root.path().join("Spotify Music.app");
        std::fs::create_dir_all(bundle.join("Contents")).unwrap();
        std::fs::write(bundle.join("Contents/Info.plist"), "fixture").unwrap();
        let settings: AppSettings = serde_json::from_value(json!({
            "Obs":{"ExecutablePath":exe},
            "Spotify":{"ExecutablePath":bundle}
        }))
        .unwrap();
        let obs = LaunchPlan::prepare(&settings, "obs", Platform::Windows).unwrap();
        assert_eq!(obs.program, std::fs::canonicalize(&exe).unwrap());
        assert!(obs.args.is_empty());
        assert_eq!(obs.working_directory, obs.program.parent().unwrap());
        let spotify = LaunchPlan::prepare(&settings, "spotify", Platform::Mac).unwrap();
        assert_eq!(spotify.program, PathBuf::from("/usr/bin/open"));
        assert_eq!(
            spotify.args,
            vec![
                OsString::from("-a"),
                std::fs::canonicalize(&bundle).unwrap().into_os_string()
            ]
        );
        assert!(LaunchPlan::prepare(&settings, "streamerbot", Platform::Windows).is_err());
        assert!(LaunchPlan::prepare(&settings, "ytmusic", Platform::Mac).is_err());
        assert!(LaunchPlan::prepare(&settings, "spotify", Platform::Windows).is_err());
    }

    #[test]
    fn missing_invalid_and_relative_paths_do_not_become_successful_launches() {
        let mut settings = AppSettings::default();
        assert!(LaunchPlan::prepare(&settings, "obs", Platform::Windows)
            .unwrap_err()
            .contains("Programmpfad"));
        settings.obs.executable_path = "obs.exe".into();
        assert!(LaunchPlan::prepare(&settings, "obs", Platform::Windows)
            .unwrap_err()
            .contains("absolut"));
        let root = tempfile::tempdir().unwrap();
        settings.obs.executable_path = root
            .path()
            .join("missing.exe")
            .to_string_lossy()
            .into_owned();
        assert!(LaunchPlan::prepare(&settings, "obs", Platform::Windows).is_err());
        let txt = root.path().join("not an executable.txt");
        std::fs::write(&txt, "text").unwrap();
        settings.obs.executable_path = txt.to_string_lossy().into_owned();
        assert!(LaunchPlan::prepare(&settings, "obs", Platform::Windows).is_err());
    }
}
