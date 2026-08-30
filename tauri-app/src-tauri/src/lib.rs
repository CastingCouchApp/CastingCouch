use ccs_core::{
    logging, AppPaths, AppSettings, JsonSettingsStore, OverlayCanvasSettings, SingleInstanceLock,
};
use ccs_modules::alerts::{AlertDefinition, AlertEngine};
use ccs_modules::obs::{ObsClient, ObsConnectOptions, ObsSceneInfo};
use ccs_modules::overlay_bridge::OverlayEventBridge;
use ccs_modules::spotify::{NowPlaying, SpotifyClient, SpotifyConnectOptions};
use ccs_modules::twitch::{TwitchClient, TwitchConnectOptions};
use ccs_modules::ServiceStatus;
use ccs_overlay_server::{OverlayServer, RealtimeHub};
use ccs_secrets::{KeyringSecretStore, SecretStore};
use serde::Serialize;
use std::sync::Arc;
use tauri::{AppHandle, Manager, State};
use tauri_plugin_opener::OpenerExt;
use tokio::sync::Mutex;
use tracing::warn;

const OBS_PASSWORD_SECRET_KEY: &str = "obs.password";

pub struct AppState {
    pub paths: AppPaths,
    pub settings: Arc<JsonSettingsStore>,
    pub secrets: Arc<KeyringSecretStore>,
    pub hub: Arc<RealtimeHub>,
    #[allow(dead_code)]
    pub overlay: Mutex<Option<OverlayServer>>,
    pub obs: Arc<ObsClient>,
    pub twitch: Arc<TwitchClient>,
    pub spotify: Arc<SpotifyClient>,
    pub alerts: AlertEngine,
    #[allow(dead_code)]
    pub bridge: OverlayEventBridge,
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
    state.settings.save(&settings).await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn list_canvases(state: State<'_, AppState>) -> Result<Vec<CanvasDto>, String> {
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    settings.overlay.ensure_canvases_migrated();
    Ok(settings
        .overlay
        .canvases
        .iter()
        .map(|c| CanvasDto {
            editor_url: settings.overlay.editor_url(&c.id),
            view_url: settings.overlay.view_url(&c.id),
            id: c.id.clone(),
            name: c.name.clone(),
        })
        .collect())
}

#[tauri::command]
async fn create_canvas(state: State<'_, AppState>, name: String) -> Result<CanvasDto, String> {
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    settings.overlay.ensure_canvases_migrated();
    let canvas = OverlayCanvasSettings {
        id: uuid::Uuid::new_v4().simple().to_string(),
        name,
    };
    let dto = CanvasDto {
        editor_url: settings.overlay.editor_url(&canvas.id),
        view_url: settings.overlay.view_url(&canvas.id),
        id: canvas.id.clone(),
        name: canvas.name.clone(),
    };
    settings.overlay.canvases.push(canvas);
    state.settings.save(&settings).await.map_err(|e| e.to_string())?;
    Ok(dto)
}

#[tauri::command]
async fn delete_canvas(state: State<'_, AppState>, id: String) -> Result<(), String> {
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    settings.overlay.canvases.retain(|c| c.id != id);
    settings.overlay.ensure_canvases_migrated();
    state.settings.save(&settings).await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn duplicate_canvas(state: State<'_, AppState>, id: String) -> Result<CanvasDto, String> {
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let source = settings
        .overlay
        .canvases
        .iter()
        .find(|c| c.id == id)
        .cloned()
        .ok_or_else(|| "canvas not found".to_string())?;
    let canvas = OverlayCanvasSettings {
        id: uuid::Uuid::new_v4().simple().to_string(),
        name: format!("{} (Kopie)", source.name),
    };
    let dto = CanvasDto {
        editor_url: settings.overlay.editor_url(&canvas.id),
        view_url: settings.overlay.view_url(&canvas.id),
        id: canvas.id.clone(),
        name: canvas.name.clone(),
    };
    settings.overlay.canvases.push(canvas);
    state.settings.save(&settings).await.map_err(|e| e.to_string())?;
    Ok(dto)
}

#[tauri::command]
async fn service_statuses(state: State<'_, AppState>) -> Result<Vec<ServiceStatus>, String> {
    Ok(vec![
        state.obs.status().await,
        state.twitch.status().await,
        state.spotify.status().await,
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
    let reconnect_seconds = settings
        .general
        .connection_watchdog_seconds
        .max(1) as u64;
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
async fn twitch_login(
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<ServiceStatus, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let options = TwitchConnectOptions {
        client_id: settings.twitch.client_id.clone(),
        channel_name: settings.twitch.channel_name.clone(),
        scopes: settings.twitch.scopes.clone(),
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
    Ok(state.alerts.list().await)
}

#[tauri::command]
async fn upsert_alert(state: State<'_, AppState>, alert: AlertDefinition) -> Result<(), String> {
    state.alerts.upsert(alert).await;
    Ok(())
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
fn overlay_health_url() -> String {
    "http://127.0.0.1:8765/health".into()
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

            let rt_settings = settings.clone();
            let rt_paths = paths.clone();
            let rt_hub = hub.clone();
            tauri::async_runtime::spawn(async move {
                match OverlayServer::start(rt_settings, rt_paths, rt_hub, 8765).await {
                    Ok(server) => {
                        tracing::info!(port = server.port, "overlay server started");
                    }
                    Err(e) => tracing::error!("overlay server failed: {e}"),
                }
            });

            let loaded = tauri::async_runtime::block_on(settings.load()).unwrap_or_default();
            let obs = ObsClient::new_shared(loaded.obs.host.clone(), loaded.obs.port);
            let twitch = TwitchClient::new_shared(secrets_dyn.clone());
            let spotify = SpotifyClient::new_shared(secrets_dyn);

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
                let reconnect_seconds =
                    loaded.general.connection_watchdog_seconds.max(1) as u64;
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
                tauri::async_runtime::spawn(async move {
                    let options = match settings_auto.load().await {
                        Ok(s) => TwitchConnectOptions {
                            client_id: s.twitch.client_id,
                            channel_name: s.twitch.channel_name,
                            scopes: s.twitch.scopes,
                        },
                        Err(_) => TwitchConnectOptions {
                            client_id,
                            channel_name,
                            scopes,
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
                overlay: Mutex::new(None),
                obs,
                twitch,
                spotify,
                alerts: AlertEngine::new(),
                bridge,
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
            service_statuses,
            connect_obs,
            disconnect_obs,
            obs_scenes,
            obs_set_scene,
            set_obs_password,
            obs_has_password,
            twitch_login,
            twitch_logout,
            spotify_login,
            spotify_logout,
            list_alerts,
            upsert_alert,
            now_playing,
            app_paths,
            overlay_health_url,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
