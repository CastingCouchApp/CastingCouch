use ccs_core::{
    apply_verified_update, evaluate_signed_check, github_releases_url, launch_installer, logging,
    parse_manifest_bytes, parse_releases_bytes, select_release, store_verified_package,
    tauri_manifest_asset_url, AppPaths, AppSettings, JsonSettingsStore, SingleInstanceLock,
    UpdateCheckResult, UpdateError, UpdatePackage, DEFAULT_GITHUB_OWNER, DEFAULT_GITHUB_REPO,
};
use ccs_modules::alerts::{AlertDefinition, AlertEngine, AlertRuntime};
use ccs_modules::obs::{ObsClient, ObsConnectOptions, ObsSceneInfo};
use ccs_modules::overlay_bridge::OverlayEventBridge;
use ccs_modules::sidecar::{
    self, SidecarStartOptions, SidecarSupervisor, WorkflowRunResponse, YtmNowPlaying,
    DEFAULT_SIDECAR_PORT, WORKFLOW_PREPARE,
};
use ccs_modules::spotify::{NowPlaying, SpotifyClient, SpotifyConnectOptions};
use ccs_modules::twitch::{TwitchClient, TwitchConnectOptions};
use ccs_modules::ServiceStatus;
use ccs_overlay_server::{OverlayCanvasService, OverlayLayoutStore, OverlayServer, RealtimeHub};
use ccs_secrets::{KeyringSecretStore, SecretStore};
use serde::Serialize;
use serde_json::json;
use std::path::PathBuf;
use std::sync::Arc;
use tauri::{AppHandle, Emitter, Manager, State, WebviewUrl, WebviewWindowBuilder};
use tauri_plugin_opener::OpenerExt;
use tokio::sync::{broadcast, Mutex};
use tracing::{error, info, warn};

const OBS_PASSWORD_SECRET_KEY: &str = "obs.password";

pub struct AppState {
    pub paths: AppPaths,
    pub settings: Arc<JsonSettingsStore>,
    pub secrets: Arc<KeyringSecretStore>,
    pub hub: Arc<RealtimeHub>,
    pub overlay: Mutex<Option<OverlayServer>>,
    pub sidecar: Arc<SidecarSupervisor>,
    pub obs: Arc<ObsClient>,
    pub twitch: Arc<TwitchClient>,
    pub spotify: Arc<SpotifyClient>,
    pub alerts: Arc<AlertEngine>,
    pub bridge: OverlayEventBridge,
    pub verified_update: Mutex<Option<PathBuf>>,
    _lock: Option<SingleInstanceLock>,
}

#[derive(Serialize)]
pub struct CanvasDto {
    pub id: String,
    pub name: String,
    pub editor_url: String,
    pub view_url: String,
}

#[tauri::command]
async fn get_settings(state: State<'_, AppState>) -> Result<AppSettings, String> {
    state.settings.load().await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn save_settings(state: State<'_, AppState>, settings: AppSettings) -> Result<(), String> {
    state
        .settings
        .save(&settings)
        .await
        .map_err(|e| e.to_string())
}

fn canvas_dto(settings: &AppSettings, id: &str, name: &str) -> CanvasDto {
    CanvasDto {
        editor_url: settings.overlay.editor_url(id),
        view_url: settings.overlay.view_url(id),
        id: id.to_string(),
        name: name.to_string(),
    }
}

fn canvas_service(state: &AppState) -> OverlayCanvasService<OverlayLayoutStore> {
    OverlayCanvasService::new(OverlayLayoutStore::new(state.paths.overlay_layouts.clone()))
}

#[tauri::command]
async fn list_canvases(state: State<'_, AppState>) -> Result<Vec<CanvasDto>, String> {
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    settings.overlay.ensure_canvases_migrated();
    Ok(settings
        .overlay
        .canvases
        .iter()
        .map(|c| canvas_dto(&settings, &c.id, &c.name))
        .collect())
}

#[tauri::command]
async fn create_canvas(state: State<'_, AppState>, name: String) -> Result<CanvasDto, String> {
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let canvas = canvas_service(&state)
        .create(&mut settings, state.settings.as_ref(), &name)
        .await
        .map_err(|e| e.to_string())?;
    Ok(canvas_dto(&settings, &canvas.id, &canvas.name))
}

#[tauri::command]
async fn delete_canvas(state: State<'_, AppState>, id: String) -> Result<(), String> {
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    canvas_service(&state)
        .delete(&mut settings, state.settings.as_ref(), &id)
        .await
        .map_err(|e| e.to_string())
}

#[tauri::command]
async fn duplicate_canvas(state: State<'_, AppState>, id: String) -> Result<CanvasDto, String> {
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let source_name = settings
        .overlay
        .canvases
        .iter()
        .find(|c| c.id.eq_ignore_ascii_case(&id))
        .map(|c| c.name.clone())
        .ok_or_else(|| format!("Overlay-Canvas '{id}' wurde nicht gefunden."))?;
    let name = format!("{source_name} (Kopie)");
    let canvas = canvas_service(&state)
        .duplicate(&mut settings, state.settings.as_ref(), &id, &name)
        .await
        .map_err(|e| e.to_string())?;
    Ok(canvas_dto(&settings, &canvas.id, &canvas.name))
}

#[tauri::command]
async fn open_overlay_editor(
    app: AppHandle,
    id: String,
    name: String,
    editor_url: String,
) -> Result<(), String> {
    let label = format!("overlay-editor-{id}");
    if let Some(existing) = app.get_webview_window(&label) {
        existing.set_focus().map_err(|e| e.to_string())?;
        return Ok(());
    }

    let title = if name.trim().is_empty() {
        "Overlay Editor".to_string()
    } else {
        format!("Overlay Editor · {}", name.trim())
    };
    let parsed = editor_url
        .parse()
        .map_err(|e| format!("Ungültige Editor-URL: {e}"))?;
    match WebviewWindowBuilder::new(&app, &label, WebviewUrl::External(parsed))
        .title(title)
        .inner_size(1280.0, 800.0)
        .min_inner_size(960.0, 600.0)
        .build()
    {
        Ok(_) => Ok(()),
        Err(e) => {
            warn!("overlay editor window failed: {e}");
            app.opener()
                .open_url(&editor_url, None::<&str>)
                .map_err(|oe| format!("Editor konnte nicht geöffnet werden: {e}; Browser: {oe}"))
        }
    }
}

#[tauri::command]
async fn service_statuses(state: State<'_, AppState>) -> Result<Vec<ServiceStatus>, String> {
    Ok(vec![
        state.obs.status().await,
        state.twitch.status().await,
        state.spotify.status().await,
        state.sidecar.status().await,
    ])
}

#[tauri::command]
async fn connect_obs(state: State<'_, AppState>) -> Result<ServiceStatus, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let password = state
        .secrets
        .get(OBS_PASSWORD_SECRET_KEY)
        .map_err(|e| e.to_string())?
        .filter(|p| !p.is_empty());
    let reconnect_seconds = settings.general.connection_watchdog_seconds.max(1) as u64;
    let options = ObsConnectOptions {
        host: settings.obs.host.clone(),
        port: settings.obs.port,
        password,
        reconnect: settings.general.reconnect_obs,
        reconnect_seconds,
    };
    let _ = state.obs.connect(options).await;
    Ok(state.obs.status().await)
}

#[tauri::command]
async fn disconnect_obs(state: State<'_, AppState>) -> Result<ServiceStatus, String> {
    state.obs.disconnect().await.map_err(|e| e.to_string())?;
    Ok(state.obs.status().await)
}

#[tauri::command]
async fn obs_scenes(state: State<'_, AppState>) -> Result<Vec<ObsSceneInfo>, String> {
    state.obs.get_scene_list().await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn obs_set_scene(state: State<'_, AppState>, scene: String) -> Result<(), String> {
    state
        .obs
        .set_current_program_scene(&scene)
        .await
        .map_err(|e| e.to_string())
}

#[tauri::command]
async fn obs_current_scene(state: State<'_, AppState>) -> Result<Option<String>, String> {
    Ok(state.obs.current_program_scene().await)
}

#[tauri::command]
fn set_obs_password(state: State<'_, AppState>, password: String) -> Result<(), String> {
    if password.is_empty() {
        state
            .secrets
            .delete(OBS_PASSWORD_SECRET_KEY)
            .map_err(|e| e.to_string())
    } else {
        state
            .secrets
            .set(OBS_PASSWORD_SECRET_KEY, &password)
            .map_err(|e| e.to_string())
    }
}

#[tauri::command]
fn obs_has_password(state: State<'_, AppState>) -> Result<bool, String> {
    let value = state
        .secrets
        .get(OBS_PASSWORD_SECRET_KEY)
        .map_err(|e| e.to_string())?;
    Ok(value.map(|v| !v.is_empty()).unwrap_or(false))
}

#[tauri::command]
async fn twitch_login(app: AppHandle, state: State<'_, AppState>) -> Result<ServiceStatus, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let options = TwitchConnectOptions {
        client_id: settings.twitch.client_id.clone(),
        channel_name: settings.twitch.channel_name.clone(),
        scopes: settings.twitch.scopes.clone(),
        enable_event_sub: settings.twitch.enable_event_sub,
    };
    let (status, verification_uri) = state
        .twitch
        .begin_login(options)
        .await
        .map_err(|e| e.to_string())?;
    if let Err(e) = app.opener().open_url(&verification_uri, None::<&str>) {
        warn!("could not open Twitch verification URI: {e}");
    }
    Ok(status)
}

#[tauri::command]
async fn twitch_logout(state: State<'_, AppState>) -> Result<ServiceStatus, String> {
    state.twitch.logout().await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn spotify_login(
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<ServiceStatus, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let redirect_uri = if settings.spotify.redirect_uri.trim().is_empty() {
        "http://127.0.0.1:43821/callback/".into()
    } else {
        settings.spotify.redirect_uri.clone()
    };
    let options = SpotifyConnectOptions {
        client_id: settings.spotify.client_id.clone(),
        redirect_uri,
        scopes: settings.spotify.scopes.clone(),
    };
    let (status, authorize_url) = state
        .spotify
        .begin_login(options)
        .await
        .map_err(|e| e.to_string())?;
    if let Err(e) = app.opener().open_url(&authorize_url, None::<&str>) {
        warn!("could not open Spotify authorization URI: {e}");
    }
    Ok(status)
}

#[tauri::command]
async fn spotify_logout(state: State<'_, AppState>) -> Result<ServiceStatus, String> {
    state.spotify.logout().await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn list_alerts(state: State<'_, AppState>) -> Result<Vec<AlertDefinition>, String> {
    state.alerts.list().await
}

#[tauri::command]
async fn upsert_alert(
    state: State<'_, AppState>,
    alert: AlertDefinition,
) -> Result<AlertDefinition, String> {
    state.alerts.upsert(alert).await
}

#[tauri::command]
async fn delete_alert(state: State<'_, AppState>, alert_type: String) -> Result<(), String> {
    state.alerts.delete(&alert_type).await
}

#[tauri::command]
async fn alert_runtime(
    state: State<'_, AppState>,
    enabled: Option<bool>,
    obs_scene_name: Option<String>,
) -> Result<AlertRuntime, String> {
    state.alerts.set_runtime(enabled, obs_scene_name).await
}

#[tauri::command]
async fn test_alert(
    state: State<'_, AppState>,
    alert_type: String,
    user: Option<String>,
) -> Result<usize, String> {
    let user = user.unwrap_or_else(|| "Test".into());
    state.alerts.test_alert(&alert_type, &user).await
}

#[tauri::command]
async fn now_playing(state: State<'_, AppState>) -> Result<NowPlaying, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    match state
        .spotify
        .refresh_now_playing(&settings.spotify.client_id)
        .await
    {
        Ok(playing) => Ok(playing),
        Err(_) => Ok(state.spotify.now_playing().await),
    }
}

#[tauri::command]
fn app_paths(state: State<'_, AppState>) -> Result<String, String> {
    Ok(state.paths.data_root.display().to_string())
}

#[tauri::command]
async fn overlay_health_url(state: State<'_, AppState>) -> Result<String, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    Ok(format!(
        "http://127.0.0.1:{}/health",
        settings.overlay.web_server_port
    ))
}

#[derive(Serialize)]
struct AppVersionInfo {
    version: String,
    channel: String,
}

fn update_http() -> Result<reqwest::Client, String> {
    reqwest::Client::builder()
        .user_agent("CreatorControlSuite")
        .build()
        .map_err(|e| e.to_string())
}

#[tauri::command]
async fn app_version(state: State<'_, AppState>) -> Result<AppVersionInfo, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    Ok(AppVersionInfo {
        version: env!("CARGO_PKG_VERSION").into(),
        channel: settings.updates.channel,
    })
}

#[tauri::command]
async fn check_updates(state: State<'_, AppState>) -> Result<UpdateCheckResult, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let current_version = env!("CARGO_PKG_VERSION");
    let channel = settings.updates.channel.clone();
    let client = update_http()?;
    let url = github_releases_url(DEFAULT_GITHUB_OWNER, DEFAULT_GITHUB_REPO);
    let response = match client.get(&url).send().await {
        Ok(response) => response,
        Err(error) => {
            return Ok(UpdateCheckResult {
                update_available: false,
                current_version: current_version.into(),
                package: None,
                detail: format!("Updateprüfung fehlgeschlagen: {error}"),
            });
        }
    };
    if !response.status().is_success() {
        return Ok(UpdateCheckResult {
            update_available: false,
            current_version: current_version.into(),
            package: None,
            detail: format!("Updateprüfung fehlgeschlagen: HTTP {}", response.status()),
        });
    }
    let bytes = response.bytes().await.map_err(|e| e.to_string())?;
    let releases = parse_releases_bytes(&bytes).map_err(|e| e.to_string())?;
    let Some(release) = select_release(&releases, &channel) else {
        return Ok(UpdateCheckResult {
            update_available: false,
            current_version: current_version.into(),
            package: None,
            detail: format!("Kein GitHub-Release für Kanal {} gefunden.", channel),
        });
    };
    let Some(manifest_url) = tauri_manifest_asset_url(release) else {
        return Ok(UpdateCheckResult {
            update_available: false,
            current_version: current_version.into(),
            package: None,
            detail: format!(
                "Release {} enthält kein {}.",
                release.tag_name,
                ccs_core::current_tauri_manifest_asset_name()
            ),
        });
    };
    let manifest_bytes = client
        .get(manifest_url)
        .send()
        .await
        .map_err(|e| e.to_string())?
        .bytes()
        .await
        .map_err(|e| e.to_string())?;
    let manifest = parse_manifest_bytes(&manifest_bytes).map_err(|e| e.to_string())?;
    Ok(evaluate_signed_check(
        current_version,
        &channel,
        &releases,
        &manifest,
    ))
}

#[tauri::command]
async fn download_update(
    state: State<'_, AppState>,
    package: UpdatePackage,
) -> Result<String, String> {
    let client = update_http()?;
    let bytes = client
        .get(&package.download_uri)
        .send()
        .await
        .map_err(|e| e.to_string())?
        .bytes()
        .await
        .map_err(|e| e.to_string())?;
    let dest = state
        .paths
        .data_root
        .join("Downloads")
        .join(&package.package_file_name);
    match store_verified_package(&dest, &package.to_manifest(), &bytes) {
        Ok(path) => {
            *state.verified_update.lock().await = Some(path.clone());
            Ok(path.display().to_string())
        }
        Err(UpdateError::ChecksumMismatch) => {
            *state.verified_update.lock().await = None;
            Err("sha256 mismatch".into())
        }
        Err(error) => {
            *state.verified_update.lock().await = None;
            Err(error.to_string())
        }
    }
}

#[tauri::command]
async fn apply_update(state: State<'_, AppState>) -> Result<String, String> {
    let verified = {
        let guard = state.verified_update.lock().await;
        guard.clone()
    };
    let Some(package) = verified else {
        return Err("Updatepaket ist nicht verifiziert.".into());
    };
    let install_dir = std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|p| p.to_path_buf()))
        .ok_or_else(|| "Installationsordner nicht ermittelbar.".to_string())?;
    let current_version = env!("CARGO_PKG_VERSION");
    apply_verified_update(
        &package,
        &install_dir,
        &state.paths.backups,
        current_version,
        launch_installer,
    )
    .map_err(|e| e.to_string())?;
    Ok("Installer gestartet. Die App kann beendet werden, sobald das Setup läuft.".into())
}

#[tauri::command]
async fn sidecar_status(state: State<'_, AppState>) -> Result<ServiceStatus, String> {
    Ok(state.sidecar.status().await)
}

#[tauri::command]
async fn sidecar_ytm_now_playing(state: State<'_, AppState>) -> Result<YtmNowPlaying, String> {
    Ok(state.sidecar.ytm_now_playing().await)
}

#[tauri::command]
async fn sidecar_workflow_run(
    state: State<'_, AppState>,
    command: String,
) -> Result<WorkflowRunResponse, String> {
    let command = if command.trim().is_empty() {
        WORKFLOW_PREPARE.to_string()
    } else {
        command
    };
    Ok(state.sidecar.run_workflow(&command).await)
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let paths = AppPaths::from_os().map_err(|e| e.to_string())?;
            paths.ensure_dirs().map_err(|e| e.to_string())?;
            let _ = logging::init_logging(&paths.logs);
            let lock = match SingleInstanceLock::acquire(&paths.lock_file) {
                Ok(lock) => Some(lock),
                Err(e) => {
                    warn!("single instance: {e}");
                    None
                }
            };
            let settings = Arc::new(JsonSettingsStore::new(paths.settings_file.clone()));
            let hub = Arc::new(RealtimeHub::new());
            let bridge = OverlayEventBridge::new(hub.clone());
            let secrets: Arc<KeyringSecretStore> = Arc::new(KeyringSecretStore::new());
            let secrets_dyn: Arc<dyn SecretStore> = secrets.clone();

            let loaded = tauri::async_runtime::block_on(settings.load()).unwrap_or_default();
            let overlay_port = loaded.overlay.web_server_port;
            let overlay_server = tauri::async_runtime::block_on(OverlayServer::start(
                settings.clone(),
                paths.clone(),
                hub.clone(),
                overlay_port,
            ));
            let overlay_failed = overlay_server.is_err();
            match &overlay_server {
                Ok(server) => info!(port = server.port, "overlay server started"),
                Err(e) => error!("overlay server failed: {e}"),
            }

            let sidecar = SidecarSupervisor::new_shared();
            spawn_sidecar(sidecar.clone(), &loaded, overlay_failed, overlay_port);

            let obs = ObsClient::new_shared(loaded.obs.host.clone(), loaded.obs.port);
            let twitch = TwitchClient::new_shared(secrets_dyn.clone());
            let spotify = SpotifyClient::new_shared(secrets_dyn);
            let alerts = Arc::new(AlertEngine::from_store(settings.clone(), bridge.clone()));

            spawn_live_event_bridges(
                app.handle().clone(),
                obs.clone(),
                twitch.clone(),
                spotify.clone(),
                bridge.clone(),
                alerts.clone(),
            );

            if loaded.obs.auto_connect {
                let obs_auto = Arc::clone(&obs);
                let settings_auto = Arc::clone(&settings);
                let password = secrets
                    .get(OBS_PASSWORD_SECRET_KEY)
                    .ok()
                    .flatten()
                    .filter(|p| !p.is_empty());
                let host = loaded.obs.host.clone();
                let port = loaded.obs.port;
                let reconnect = loaded.general.reconnect_obs;
                let reconnect_seconds = loaded.general.connection_watchdog_seconds.max(1) as u64;
                tauri::async_runtime::spawn(async move {
                    let (host, port, reconnect, reconnect_seconds) =
                        match settings_auto.load().await {
                            Ok(s) => (
                                s.obs.host,
                                s.obs.port,
                                s.general.reconnect_obs,
                                s.general.connection_watchdog_seconds.max(1) as u64,
                            ),
                            Err(_) => (host, port, reconnect, reconnect_seconds),
                        };
                    let _ = obs_auto
                        .connect(ObsConnectOptions {
                            host,
                            port,
                            password,
                            reconnect,
                            reconnect_seconds,
                        })
                        .await;
                });
            }

            if loaded.twitch.auto_connect
                && !loaded.twitch.client_id.trim().is_empty()
                && twitch.has_token()
            {
                let twitch_auto = Arc::clone(&twitch);
                let settings_auto = Arc::clone(&settings);
                let client_id = loaded.twitch.client_id.clone();
                let channel_name = loaded.twitch.channel_name.clone();
                let scopes = loaded.twitch.scopes.clone();
                let enable_event_sub = loaded.twitch.enable_event_sub;
                tauri::async_runtime::spawn(async move {
                    let options = match settings_auto.load().await {
                        Ok(s) => TwitchConnectOptions {
                            client_id: s.twitch.client_id,
                            channel_name: s.twitch.channel_name,
                            scopes: s.twitch.scopes,
                            enable_event_sub: s.twitch.enable_event_sub,
                        },
                        Err(_) => TwitchConnectOptions {
                            client_id,
                            channel_name,
                            scopes,
                            enable_event_sub,
                        },
                    };
                    if let Err(e) = twitch_auto.connect(&options).await {
                        warn!("twitch auto-connect failed: {e}");
                    }
                });
            }

            if loaded.spotify.auto_connect
                && !loaded.spotify.client_id.trim().is_empty()
                && spotify.has_token()
            {
                let spotify_auto = Arc::clone(&spotify);
                let settings_auto = Arc::clone(&settings);
                let client_id = loaded.spotify.client_id.clone();
                let redirect_uri = loaded.spotify.redirect_uri.clone();
                let scopes = loaded.spotify.scopes.clone();
                tauri::async_runtime::spawn(async move {
                    let options = match settings_auto.load().await {
                        Ok(s) => SpotifyConnectOptions {
                            client_id: s.spotify.client_id,
                            redirect_uri: if s.spotify.redirect_uri.trim().is_empty() {
                                "http://127.0.0.1:43821/callback/".into()
                            } else {
                                s.spotify.redirect_uri
                            },
                            scopes: s.spotify.scopes,
                        },
                        Err(_) => SpotifyConnectOptions {
                            client_id,
                            redirect_uri,
                            scopes,
                        },
                    };
                    match spotify_auto.connect(&options).await {
                        Ok(_) => spotify_auto.spawn_poll(options.client_id),
                        Err(e) => warn!("spotify auto-connect failed: {e}"),
                    }
                });
            }

            app.manage(AppState {
                paths,
                settings,
                secrets,
                hub,
                overlay: Mutex::new(overlay_server.ok()),
                sidecar,
                obs,
                twitch,
                spotify,
                alerts,
                bridge,
                verified_update: Mutex::new(None),
                _lock: lock,
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_settings,
            save_settings,
            list_canvases,
            create_canvas,
            delete_canvas,
            duplicate_canvas,
            open_overlay_editor,
            service_statuses,
            connect_obs,
            disconnect_obs,
            obs_scenes,
            obs_set_scene,
            obs_current_scene,
            set_obs_password,
            obs_has_password,
            twitch_login,
            twitch_logout,
            spotify_login,
            spotify_logout,
            list_alerts,
            upsert_alert,
            delete_alert,
            alert_runtime,
            test_alert,
            now_playing,
            app_paths,
            overlay_health_url,
            app_version,
            check_updates,
            download_update,
            apply_update,
            sidecar_status,
            sidecar_ytm_now_playing,
            sidecar_workflow_run,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

fn spawn_sidecar(
    sidecar: Arc<SidecarSupervisor>,
    loaded: &AppSettings,
    overlay_failed: bool,
    overlay_port: u16,
) {
    let enabled = loaded.sidecar.enabled || sidecar::env_enabled();
    let port = if loaded.sidecar.port == 0 {
        DEFAULT_SIDECAR_PORT
    } else {
        loaded.sidecar.port
    };
    let explicit = {
        let trimmed = loaded.sidecar.binary_path.trim();
        if trimmed.is_empty() {
            None
        } else {
            Some(PathBuf::from(trimmed))
        }
    };
    let binary = sidecar::resolve_binary(explicit.as_deref(), &sidecar_search_dirs());
    tauri::async_runtime::spawn(async move {
        let status = sidecar
            .start(SidecarStartOptions {
                enabled,
                port,
                overlay_failed,
                overlay_port,
                binary,
                is_windows: cfg!(windows),
            })
            .await;
        match status.state {
            ccs_modules::ConnectionState::Connected => {
                info!(detail = %status.detail, "sidecar connected");
            }
            ccs_modules::ConnectionState::Error => {
                warn!(detail = %status.detail, "sidecar not started");
            }
            _ => {}
        }
    });
}

fn sidecar_search_dirs() -> Vec<PathBuf> {
    let mut dirs = Vec::new();
    if let Ok(exe) = std::env::current_exe() {
        if let Some(parent) = exe.parent() {
            dirs.push(parent.to_path_buf());
        }
    }
    dirs
}

fn spawn_live_event_bridges(
    app: AppHandle,
    obs: Arc<ObsClient>,
    twitch: Arc<TwitchClient>,
    spotify: Arc<SpotifyClient>,
    bridge: OverlayEventBridge,
    alerts: Arc<AlertEngine>,
) {
    let mut twitch_events = twitch.subscribe_events();
    let twitch_status = twitch.subscribe_status();
    let obs_status = obs.subscribe_status();
    let mut obs_scenes = obs.subscribe_scenes();
    let spotify_status = spotify.subscribe_status();
    let mut spotify_now_playing = spotify.subscribe_now_playing();

    let app_twitch_evt = app.clone();
    let bridge_twitch = bridge.clone();
    let alerts_twitch = alerts.clone();
    tauri::async_runtime::spawn(async move {
        loop {
            match twitch_events.recv().await {
                Ok(evt) => {
                    let overlay = bridge_twitch.from_twitch(
                        &evt.event_type,
                        &evt.summary,
                        evt.received_at,
                        evt.data.clone(),
                    );
                    alerts_twitch
                        .enqueue_matching(&evt.event_type, &evt.data)
                        .await;
                    let _ = app_twitch_evt.emit("twitch-event", &overlay);
                }
                Err(broadcast::error::RecvError::Lagged(_)) => continue,
                Err(broadcast::error::RecvError::Closed) => break,
            }
        }
    });

    spawn_status_forward(app.clone(), twitch_status);
    spawn_status_forward(app.clone(), obs_status);
    spawn_status_forward(app.clone(), spotify_status);

    let app_np = app.clone();
    tauri::async_runtime::spawn(async move {
        loop {
            match spotify_now_playing.recv().await {
                Ok(playing) => {
                    let _ = app_np.emit("now-playing", &playing);
                }
                Err(broadcast::error::RecvError::Lagged(_)) => continue,
                Err(broadcast::error::RecvError::Closed) => break,
            }
        }
    });

    let app_obs = app;
    let bridge_obs = bridge;
    tauri::async_runtime::spawn(async move {
        loop {
            match obs_scenes.recv().await {
                Ok(scene) => {
                    let overlay = bridge_obs.app_obs_scene(&scene);
                    let _ = app_obs.emit("obs-scene", json!({ "scene": scene }));
                    let _ = overlay;
                }
                Err(broadcast::error::RecvError::Lagged(_)) => continue,
                Err(broadcast::error::RecvError::Closed) => break,
            }
        }
    });
}

fn spawn_status_forward(app: AppHandle, mut rx: broadcast::Receiver<ServiceStatus>) {
    tauri::async_runtime::spawn(async move {
        loop {
            match rx.recv().await {
                Ok(status) => {
                    let _ = app.emit("service-status", &status);
                }
                Err(broadcast::error::RecvError::Lagged(_)) => continue,
                Err(broadcast::error::RecvError::Closed) => break,
            }
        }
    });
}
