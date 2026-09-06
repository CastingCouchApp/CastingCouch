mod runtime;
use ccs_core::{
    apply_verified_update, evaluate_signed_check, github_releases_url, launch_installer, logging,
    parse_manifest_bytes, parse_releases_bytes, select_release, store_verified_package,
    tauri_manifest_asset_url, AppPaths, AppSettings, JsonSettingsStore, SingleInstanceLock,
    UpdateCheckResult, UpdateError, UpdatePackage, DEFAULT_GITHUB_OWNER, DEFAULT_GITHUB_REPO,
};
use ccs_modules::alerts::{AlertDefinition, AlertEngine, AlertRuntime};
use ccs_modules::obs::{ObsClient, ObsConnectOptions, ObsControl, ObsSceneInfo};
use ccs_modules::overlay_bridge::OverlayEventBridge;
use ccs_modules::spotify::{NowPlaying, SpotifyClient, SpotifyConnectOptions};
use ccs_modules::twitch::{TwitchClient, TwitchConnectOptions};
use ccs_modules::ServiceStatus;
use ccs_overlay_server::{OverlayCanvasService, OverlayLayoutStore, OverlayServer, RealtimeHub};
use ccs_secrets::{KeyringSecretStore, SecretStore};
use runtime::spawn_runtime;
use serde::Serialize;
use serde_json::{json, Value};
use std::path::PathBuf;
use std::sync::Arc;
use tauri::{AppHandle, Emitter, Manager, State, WebviewUrl, WebviewWindowBuilder};
use tauri_plugin_opener::OpenerExt;
use tokio::sync::{broadcast, Mutex};
use tracing::{error, info, warn};

const OBS_PASSWORD_SECRET_KEY: &str = "obs.password";

pub struct AppState {
    pub ytm: Mutex<Option<Arc<ccs_overlay_server::YouTubeMusicBridge>>>,
    pub paths: AppPaths,
    pub settings_mutation: Mutex<()>,
    pub settings: Arc<JsonSettingsStore>,
    pub secrets: Arc<KeyringSecretStore>,
    pub hub: Arc<RealtimeHub>,
    pub overlay: Mutex<Option<OverlayServer>>,
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
async fn open_twitch_chat(app: AppHandle, state: State<'_, AppState>) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("twitch-chat") {
        return window.set_focus().map_err(|e| e.to_string());
    }
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let channel = if settings.twitch.channel_name.trim().is_empty() {
        state
            .twitch
            .current_user()
            .await
            .ok_or("Twitch nicht verbunden")?
            .login
    } else {
        settings.twitch.channel_name
    };
    if !channel
        .bytes()
        .all(|b| b.is_ascii_alphanumeric() || b == b'_')
    {
        return Err("Ungültiger Twitch-Kanal".into());
    }
    let url = format!("https://www.twitch.tv/popout/{channel}/chat?popout=")
        .parse()
        .map_err(|e| format!("{e}"))?;
    let builder = WebviewWindowBuilder::new(&app, "twitch-chat", WebviewUrl::External(url))
        .title("Twitch-Chat")
        .inner_size(450.0, 800.0);
    #[cfg(target_os = "windows")]
    let builder = builder.data_directory(state.paths.data_root.join("WebView/Twitch"));
    builder.build().map_err(|e| e.to_string())?;
    Ok(())
}
#[tauri::command]
async fn twitch_action(
    state: State<'_, AppState>,
    action: ccs_modules::twitch::TwitchAction,
) -> Result<Value, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    if !settings.twitch.enable_chat
        && matches!(action, ccs_modules::twitch::TwitchAction::SendChat { .. })
    {
        return Err("Twitch-Chat ist deaktiviert.".into());
    }
    state
        .twitch
        .action(
            &settings.twitch.client_id,
            &settings.twitch.channel_name,
            action,
        )
        .await
        .map_err(|e| e.to_string())
}
#[tauri::command]
async fn twitch_query(
    state: State<'_, AppState>,
    query: ccs_modules::twitch::TwitchQuery,
    after: Option<String>,
) -> Result<Value, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    state
        .twitch
        .query(
            &settings.twitch.client_id,
            &settings.twitch.channel_name,
            query,
            after,
        )
        .await
        .map_err(|e| e.to_string())
}
#[tauri::command]
async fn chat_history(state: State<'_, AppState>) -> Result<Value, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    Ok(if settings.twitch.enable_chat {
        state.hub.history()
    } else {
        json!({"events":[]})
    })
}
#[tauri::command]
fn countdown_status(state: State<'_, AppState>) -> Value {
    state.hub.countdown()
}
#[tauri::command]
fn set_countdown(state: State<'_, AppState>, seconds: i64, label: String) -> Result<(), String> {
    state.hub.set_countdown(seconds, &label)
}

#[tauri::command]
async fn spotify_action(
    state: State<'_, AppState>,
    action: ccs_modules::spotify::SpotifyAction,
) -> Result<Value, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    state
        .spotify
        .action(&settings.spotify.client_id, action)
        .await
        .map_err(|e| e.to_string())
}
#[tauri::command]
async fn spotify_query(
    state: State<'_, AppState>,
    query: ccs_modules::spotify::SpotifyQuery,
    offset: Option<u64>,
) -> Result<Value, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    state
        .spotify
        .query(&settings.spotify.client_id, query, offset)
        .await
        .map_err(|e| e.to_string())
}
#[tauri::command]
async fn setup_overlay_source(
    state: State<'_, AppState>,
    canvas_id: String,
    scene_name: String,
    input_name: String,
) -> Result<Value, String> {
    let settings = state.settings.load().await.map_err(|e| e.to_string())?;
    if !settings.overlay.canvases.iter().any(|c| c.id == canvas_id) {
        return Err("Canvas existiert nicht".into());
    }
    let layout = OverlayLayoutStore::new(state.paths.overlay_layouts.clone())
        .load(&canvas_id)
        .await
        .map_err(|e| e.to_string())?;
    let width = layout["canvasWidth"]
        .as_u64()
        .and_then(|n| u32::try_from(n).ok())
        .ok_or("Canvas-Breite fehlt")?;
    let height = layout["canvasHeight"]
        .as_u64()
        .and_then(|n| u32::try_from(n).ok())
        .ok_or("Canvas-Höhe fehlt")?;
    state
        .obs
        .ensure_overlay_source(
            &scene_name,
            &input_name,
            &settings.overlay.view_url(&canvas_id),
            width,
            height,
        )
        .await
        .map_err(|e| e.to_string())
}

#[tauri::command]
async fn obs_query(
    state: State<'_, AppState>,
    query: ccs_modules::obs::ObsQuery,
) -> Result<Value, String> {
    state.obs.query(query).await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn obs_control(state: State<'_, AppState>, control: ObsControl) -> Result<Value, String> {
    state.obs.control(control).await.map_err(|e| e.to_string())
}
#[tauri::command]
async fn obs_output_status(state: State<'_, AppState>) -> Result<Value, String> {
    state.obs.output_status().await.map_err(|e| e.to_string())
}
#[tauri::command]
async fn ytm_connect(state: State<'_, AppState>) -> Result<String, String> {
    let mut current = state.ytm.lock().await;
    if current.is_none() {
        let settings = state.settings.load().await.map_err(|e| e.to_string())?;
        let port = settings
            .you_tube_music
            .extra
            .get("BridgePort")
            .and_then(Value::as_u64)
            .unwrap_or(43831);
        let port = u16::try_from(port).map_err(|_| "Ungültiger YouTube-Music-Port")?;
        if port == 0 {
            return Err("YouTube-Music-Port muss positiv sein".into());
        }
        *current = Some(ccs_overlay_server::YouTubeMusicBridge::start(port).await?);
    }
    Ok(format!(
        "http://127.0.0.1:{}/ytmusic/install",
        current.as_ref().unwrap().port
    ))
}
#[tauri::command]
async fn ytm_disconnect(state: State<'_, AppState>) -> Result<(), String> {
    if let Some(bridge) = state.ytm.lock().await.take() {
        bridge.stop();
    }
    Ok(())
}
#[tauri::command]
async fn ytm_now_playing(
    state: State<'_, AppState>,
) -> Result<ccs_overlay_server::MusicSnapshot, String> {
    Ok(state
        .ytm
        .lock()
        .await
        .as_ref()
        .map(|bridge| bridge.snapshot())
        .unwrap_or_else(|| ccs_overlay_server::MusicSnapshot {
            provider: "ytmusic".into(),
            status_text: "Bridge gestoppt".into(),
            ..Default::default()
        }))
}
#[tauri::command]
async fn ytm_command(state: State<'_, AppState>, command: String) -> Result<(), String> {
    state
        .ytm
        .lock()
        .await
        .as_ref()
        .ok_or("YouTube Music nicht verbunden")?
        .command(&command)
}

#[tauri::command]
async fn get_settings(state: State<'_, AppState>) -> Result<Value, String> {
    state.settings.read_value().await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn save_settings(
    state: State<'_, AppState>,
    settings: Value,
    original: Value,
    obs_password: Option<String>,
) -> Result<Value, String> {
    let _guard = state.settings_mutation.lock().await;
    let requested: AppSettings =
        serde_json::from_value(settings.clone()).map_err(|e| e.to_string())?;
    ccs_core::store::validate_settings(&requested).map_err(|e| e.to_string())?;
    let old = state.settings.load().await.map_err(|e| e.to_string())?;
    let original_port = original
        .pointer("/Overlay/WebServerPort")
        .and_then(Value::as_u64);
    let port_changed = original_port != Some(requested.overlay.web_server_port as u64);
    let replacement =
        if port_changed && requested.overlay.web_server_port != old.overlay.web_server_port {
            Some(
                OverlayServer::start(
                    state.settings.clone(),
                    state.paths.clone(),
                    state.hub.clone(),
                    requested.overlay.web_server_port,
                )
                .await
                .map_err(|e| e.to_string())?,
            )
        } else {
            None
        };
    let saved = match state.settings.save_edit(&original, &settings).await {
        Ok(saved) => saved,
        Err(error) => {
            if let Some(server) = replacement {
                server.stop();
            }
            return Err(error.to_string());
        }
    };
    if let Some(server) = replacement {
        if let Some(previous) = state.overlay.lock().await.replace(server) {
            previous.stop();
        }
        state.hub.live.data.write().unwrap()["serverError"] = Value::Null;
    }
    let next: AppSettings = serde_json::from_value(saved).map_err(|e| e.to_string())?;
    let mut warnings = Vec::<String>::new();
    let mut password_updated = false;
    if let Some(password) = obs_password.filter(|p| !p.is_empty()) {
        match state.secrets.set(OBS_PASSWORD_SECRET_KEY, &password) {
            Ok(()) => password_updated = true,
            Err(error) => warnings.push(format!(
                "Einstellungen gespeichert; OBS-Passwort konnte nicht gespeichert werden: {error}"
            )),
        }
    }
    let obs_changed = password_updated
        || old.obs.host != next.obs.host
        || old.obs.port != next.obs.port
        || old.general.reconnect_obs != next.general.reconnect_obs
        || old.general.connection_watchdog_enabled != next.general.connection_watchdog_enabled
        || old.general.connection_watchdog_seconds != next.general.connection_watchdog_seconds;
    if obs_changed && state.obs.status().await.state != ccs_modules::ConnectionState::Disconnected {
        let connection = match state.secrets.get(OBS_PASSWORD_SECRET_KEY) {
            Ok(password) => state
                .obs
                .connect(ObsConnectOptions {
                    host: next.obs.host.clone(),
                    port: next.obs.port,
                    password,
                    reconnect: next.general.connection_watchdog_enabled
                        && next.general.reconnect_obs,
                    reconnect_seconds: next.general.connection_watchdog_seconds.max(1) as u64,
                })
                .await
                .map_err(|e| e.to_string()),
            Err(error) => Err(error.to_string()),
        };
        if let Err(e) = connection {
            warnings.push(format!(
                "Einstellungen gespeichert; OBS-Verbindung fehlgeschlagen: {e}"
            ));
        }
    }
    if (old.twitch.client_id != next.twitch.client_id
        || old.twitch.channel_name != next.twitch.channel_name
        || old.twitch.enable_event_sub != next.twitch.enable_event_sub)
        && state.twitch.current_user().await.is_some()
    {
        if let Err(e) = state
            .twitch
            .connect(&TwitchConnectOptions {
                client_id: next.twitch.client_id.clone(),
                channel_name: next.twitch.channel_name.clone(),
                scopes: next.twitch.scopes.clone(),
                enable_event_sub: next.twitch.enable_event_sub,
            })
            .await
        {
            warnings.push(format!(
                "Einstellungen gespeichert; Twitch-Verbindung fehlgeschlagen: {e}"
            ));
        }
    }
    if old.spotify.client_id != next.spotify.client_id
        && state.spotify.status().await.state != ccs_modules::ConnectionState::Disconnected
    {
        if let Err(e) = state
            .spotify
            .connect(&SpotifyConnectOptions {
                client_id: next.spotify.client_id.clone(),
                redirect_uri: next.spotify.redirect_uri.clone(),
                scopes: next.spotify.scopes.clone(),
            })
            .await
        {
            warnings.push(format!(
                "Einstellungen gespeichert; Spotify-Verbindung fehlgeschlagen: {e}"
            ));
        } else {
            state.spotify.spawn_poll(next.spotify.client_id.clone());
        }
    }
    Ok(json!({"saved":true,"warnings":warnings}))
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
    let _guard = state.settings_mutation.lock().await;
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let canvas = canvas_service(&state)
        .create(&mut settings, state.settings.as_ref(), &name)
        .await
        .map_err(|e| e.to_string())?;
    Ok(canvas_dto(&settings, &canvas.id, &canvas.name))
}

#[tauri::command]
async fn delete_canvas(state: State<'_, AppState>, id: String) -> Result<(), String> {
    let _guard = state.settings_mutation.lock().await;
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    canvas_service(&state)
        .delete(&mut settings, state.settings.as_ref(), &id)
        .await
        .map_err(|e| e.to_string())
}

#[tauri::command]
async fn duplicate_canvas(state: State<'_, AppState>, id: String) -> Result<CanvasDto, String> {
    let _guard = state.settings_mutation.lock().await;
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
async fn update_canvas(
    state: State<'_, AppState>,
    id: String,
    name: Option<String>,
    selected: Option<bool>,
) -> Result<CanvasDto, String> {
    let _guard = state.settings_mutation.lock().await;
    let mut settings = state.settings.load().await.map_err(|e| e.to_string())?;
    let canvas = canvas_service(&state)
        .update(
            &mut settings,
            state.settings.as_ref(),
            &id,
            name.as_deref(),
            selected.unwrap_or(false),
        )
        .await
        .map_err(|e| e.to_string())?;
    Ok(canvas_dto(&settings, &canvas.id, &canvas.name))
}

#[tauri::command]
async fn open_overlay_editor<R: tauri::Runtime>(
    app: AppHandle<R>,
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
        reconnect: settings.general.connection_watchdog_enabled && settings.general.reconnect_obs,
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
async fn alert_install_sources(
    state: State<'_, AppState>,
    alert_type: String,
) -> Result<(), String> {
    state.alerts.install_sources(&alert_type).await
}
#[tauri::command]
async fn alert_stop(state: State<'_, AppState>) -> Result<(), String> {
    state.alerts.stop_current();
    Ok(())
}
#[tauri::command]
async fn alert_clear_queue(state: State<'_, AppState>) -> Result<(), String> {
    state.alerts.clear_queue().await;
    Ok(())
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
    let _guard = state.settings_mutation.lock().await;
    state.alerts.upsert(alert).await
}

#[tauri::command]
async fn delete_alert(state: State<'_, AppState>, alert_type: String) -> Result<(), String> {
    let _guard = state.settings_mutation.lock().await;
    state.alerts.delete(&alert_type).await
}

#[tauri::command]
async fn alert_runtime(
    state: State<'_, AppState>,
    enabled: Option<bool>,
    obs_scene_name: Option<String>,
) -> Result<AlertRuntime, String> {
    let _guard = state.settings_mutation.lock().await;
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
    Ok(state.spotify.now_playing().await)
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

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .manage(StartupState::default())
        .setup(|app| {
            if let Err(error) = initialize(app) {
                if error
                    .downcast_ref::<ccs_core::instance::InstanceError>()
                    .is_some_and(|e| matches!(e, ccs_core::instance::InstanceError::AlreadyRunning))
                {
                    app.handle().exit(0);
                    return Ok(());
                }
                error!(%error,"App-Start fehlgeschlagen");
                *app.state::<StartupState>().0.lock().unwrap() = Some(error.to_string());
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            startup_error,
            overlay_runtime_status,
            open_twitch_chat,
            twitch_action,
            twitch_query,
            chat_history,
            countdown_status,
            set_countdown,
            spotify_action,
            spotify_query,
            obs_control,
            obs_query,
            setup_overlay_source,
            obs_output_status,
            ytm_connect,
            ytm_disconnect,
            ytm_now_playing,
            ytm_command,
            get_settings,
            save_settings,
            list_canvases,
            create_canvas,
            delete_canvas,
            duplicate_canvas,
            update_canvas,
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
            alert_install_sources,
            alert_stop,
            alert_clear_queue,
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
        ])
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|app, event| {
            if matches!(event, tauri::RunEvent::Exit) {
                if let Some(state) = app.try_state::<AppState>() {
                    if let Err(error) = state.hub.flush_history() {
                        error!(%error,"Chat-Verlauf konnte nicht gespeichert werden");
                    }
                }
            }
        });
}

#[derive(Default)]
struct StartupState(std::sync::Mutex<Option<String>>);
#[tauri::command]
fn startup_error(state: State<'_, StartupState>) -> Option<String> {
    state.0.lock().unwrap().clone()
}
#[tauri::command]
async fn overlay_runtime_status(state: State<'_, AppState>) -> Result<Value, String> {
    let server = state.overlay.lock().await;
    let settings = state.settings.load().await.ok();
    let data = state.hub.live.data.read().unwrap().clone();
    Ok(
        json!({"running":server.as_ref().is_some_and(|s|s.is_running()),"port":server.as_ref().map(|s|s.port),"configuredPort":settings.map(|s|s.overlay.web_server_port),"error":data.get("serverError").filter(|v|!v.is_null()).or_else(||data.get("dataError")).cloned().unwrap_or(Value::Null)}),
    )
}
fn initialize(app: &mut tauri::App) -> Result<(), Box<dyn std::error::Error>> {
    let paths = AppPaths::from_os().map_err(|e| e.to_string())?;
    paths.ensure_dirs().map_err(|e| e.to_string())?;
    let _ = logging::init_logging(&paths.logs);
    let lock = Some(SingleInstanceLock::acquire(&paths.lock_file)?);
    let settings = Arc::new(JsonSettingsStore::new(paths.settings_file.clone()));
    let hub = Arc::new(RealtimeHub::new());
    let bridge = OverlayEventBridge::new(hub.clone());
    let secrets: Arc<KeyringSecretStore> = Arc::new(KeyringSecretStore::new());
    let secrets_dyn: Arc<dyn SecretStore> = secrets.clone();

    let loaded = tauri::async_runtime::block_on(settings.load())?;
    let overlay_port = loaded.overlay.web_server_port;
    let overlay_server = tauri::async_runtime::block_on(OverlayServer::start(
        settings.clone(),
        paths.clone(),
        hub.clone(),
        overlay_port,
    ));
    match &overlay_server {
        Ok(server) => info!(port = server.port, "overlay server started"),
        Err(e) => {
            error!("overlay server failed: {e}");
            hub.live.data.write().unwrap()["serverError"] = json!(format!(
                "Overlay-Port {overlay_port} konnte nicht geöffnet werden: {e}"
            ));
        }
    }

    let obs = ObsClient::new_shared(loaded.obs.host.clone(), loaded.obs.port);
    *hub.obs.write().unwrap() = Some(obs.clone());
    let twitch = TwitchClient::new_shared(secrets_dyn.clone());
    let spotify = SpotifyClient::new_shared(secrets_dyn);
    let alerts = Arc::new(AlertEngine::from_store(settings.clone(), bridge.clone()));
    alerts.attach_obs(obs.clone());

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
        let reconnect = loaded.general.connection_watchdog_enabled && loaded.general.reconnect_obs;
        let reconnect_seconds = loaded.general.connection_watchdog_seconds.max(1) as u64;
        tauri::async_runtime::spawn(async move {
            let (host, port, reconnect, reconnect_seconds) = match settings_auto.load().await {
                Ok(s) => (
                    s.obs.host,
                    s.obs.port,
                    s.general.connection_watchdog_enabled && s.general.reconnect_obs,
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
        ytm: Mutex::new(None),
        paths,
        settings_mutation: Mutex::new(()),
        settings,
        secrets,
        hub,
        overlay: Mutex::new(overlay_server.ok()),
        obs,
        twitch,
        spotify,
        alerts,
        bridge,
        verified_update: Mutex::new(None),
        _lock: lock,
    });
    spawn_runtime(app.handle().clone());
    Ok(())
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
                    if evt.event_type == "channel.chat.message" {
                        if let Some(state) = app_twitch_evt.try_state::<AppState>() {
                            if state
                                .settings
                                .load()
                                .await
                                .is_ok_and(|s| !s.twitch.enable_chat)
                            {
                                continue;
                            }
                        }
                    }
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

#[cfg(test)]
mod command_tests;
