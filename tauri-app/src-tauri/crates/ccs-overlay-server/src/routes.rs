use crate::assets;
use crate::layout_store::OverlayLayoutStore;
use crate::state::OverlayState;
use axum::extract::ws::WebSocketUpgrade;
use axum::extract::{Path, Query, State};
use axum::http::{header, HeaderValue, StatusCode};
use axum::response::{Html, IntoResponse, Response};
use axum::routing::{delete, get, post};
use axum::{Json, Router};
use serde::Deserialize;
use serde_json::{json, Value};
use tokio::fs;
use tower_http::cors::{Any, CorsLayer};

pub fn router(state: OverlayState) -> Router {
    Router::new()
        .route("/health", get(health))
        .route("/ws", get(ws_upgrade))
        .route("/layout/{instance_id}", get(get_layout).put(put_layout))
        .route("/data/overlay-data.json", get(overlay_data))
        .route("/data/overlay-config.json", get(overlay_config))
        .route("/canvas/size-presets", get(size_presets))
        .route("/canvas/{*asset_path}", get(canvas_asset))
        .route("/editor", get(|| html_kind("editor")))
        .route("/view", get(|| html_kind("view")))
        .route("/editor/{instance_id}", get(|Path(_id): Path<String>| html_kind("editor")))
        .route("/view/{instance_id}", get(|Path(_id): Path<String>| html_kind("view")))
        .route("/w/{ty}", get(|Path(_ty): Path<String>| html_kind("solo")))
        .route("/w/shape/{*shape_id}", get(|Path(_id): Path<String>| html_kind("solo")))
        .route("/extensions", get(list_extensions))
        .route("/extensions/install", post(install_extension))
        .route("/extensions/{pack_id}", delete(delete_extension))
        .route("/ext/{pack_id}/{*path}", get(ext_asset))
        .route("/assets", get(list_assets).post(upload_asset))
        .route("/assets/{*id}", delete(delete_asset))
        .route("/obs/video-settings", get(obs_video_settings))
        .route("/obs/preview", get(obs_preview))
        .route("/chat", get(chat_index))
        .route("/chat/config", get(chat_config))
        .route("/chat/history", get(chat_history))
        .route("/chat/background", get(chat_background))
        .with_state(state)
        .layer(
            CorsLayer::new()
                .allow_origin(Any)
                .allow_methods(Any)
                .allow_headers(Any),
        )
}

async fn health(State(state): State<OverlayState>) -> impl IntoResponse {
    let mut settings = state.settings.load().await.unwrap_or_default();
    settings.overlay.ensure_canvases_migrated();
    settings.overlay.web_server_port = 8765;
    let selected = settings.overlay.selected_canvas();
    let canvases: Vec<Value> = settings
        .overlay
        .canvases
        .iter()
        .map(|c| {
            json!({
                "id": c.id,
                "name": c.name,
                "editorUrl": settings.overlay.editor_url(&c.id),
                "viewUrl": settings.overlay.view_url(&c.id),
            })
        })
        .collect();
    let widgets: Vec<Value> = assets::list_widget_types()
        .into_iter()
        .map(|t| json!({ "type": t, "url": settings.overlay.widget_url(&t) }))
        .chain(assets::list_shape_types().into_iter().map(|t| {
            json!({ "type": t, "url": settings.overlay.widget_url(&format!("shape/{t}")) })
        }))
        .collect();

    Json(json!({
        "ok": true,
        "port": settings.overlay.web_server_port,
        "root": state.paths.overlay_root,
        "clients": state.hub.connected_clients(),
        "baseUrl": format!("http://127.0.0.1:{}", settings.overlay.web_server_port),
        "canvasId": selected.id,
        "canvases": canvases,
        "editorUrl": settings.overlay.editor_url(&selected.id),
        "viewUrl": settings.overlay.view_url(&selected.id),
        "widgets": widgets,
    }))
}

async fn ws_upgrade(
    ws: WebSocketUpgrade,
    State(state): State<OverlayState>,
) -> impl IntoResponse {
    ws.on_upgrade(move |socket| async move {
        state.hub.handle_socket(socket).await;
    })
}

async fn get_layout(
    Path(instance_id): Path<String>,
    State(state): State<OverlayState>,
) -> Response {
    let store = OverlayLayoutStore::new(&state.paths.overlay_layouts);
    match store.read_bytes(&instance_id).await {
        Ok(Some(bytes)) => (
            [(header::CONTENT_TYPE, HeaderValue::from_static("application/json"))],
            bytes,
        )
            .into_response(),
        Ok(None) | Err(_) => Json(json!({
            "id": instance_id,
            "width": 1920,
            "height": 1080,
            "items": []
        }))
        .into_response(),
    }
}

async fn put_layout(
    Path(instance_id): Path<String>,
    State(state): State<OverlayState>,
    Json(body): Json<Value>,
) -> Result<impl IntoResponse, StatusCode> {
    let store = OverlayLayoutStore::new(&state.paths.overlay_layouts);
    store
        .save(&instance_id, &body)
        .await
        .map_err(|_| StatusCode::BAD_REQUEST)?;
    state.hub.publish(&json!({
        "type": "app.overlay.layout",
        "id": instance_id,
    }));
    Ok(StatusCode::NO_CONTENT)
}

async fn overlay_data(State(state): State<OverlayState>) -> Response {
    match fs::read(&state.overlay_data).await {
        Ok(bytes) => (
            [(header::CONTENT_TYPE, HeaderValue::from_static("application/json"))],
            bytes,
        )
            .into_response(),
        Err(_) => Json(json!({})).into_response(),
    }
}

async fn overlay_config() -> impl IntoResponse {
    Json(json!({ "ok": true }))
}

async fn size_presets() -> impl IntoResponse {
    Json(json!([
        { "id": "1080p", "width": 1920, "height": 1080 },
        { "id": "720p", "width": 1280, "height": 720 },
        { "id": "vertical", "width": 1080, "height": 1920 }
    ]))
}

async fn canvas_asset(Path(asset_path): Path<String>) -> Response {
    if let Some((bytes, mime)) = assets::get_asset(&asset_path) {
        return (
            [(
                header::CONTENT_TYPE,
                HeaderValue::from_str(&mime)
                    .unwrap_or(HeaderValue::from_static("application/octet-stream")),
            )],
            bytes,
        )
            .into_response();
    }
    StatusCode::NOT_FOUND.into_response()
}

async fn html_kind(kind: &'static str) -> Response {
    match assets::html_shell(kind) {
        Some(html) => Html(html.to_string()).into_response(),
        None => StatusCode::NOT_FOUND.into_response(),
    }
}

async fn list_extensions(State(state): State<OverlayState>) -> impl IntoResponse {
    let dir = state.paths.overlay_root.join("extensions");
    let mut packs = vec![];
    if let Ok(mut rd) = fs::read_dir(&dir).await {
        while let Ok(Some(entry)) = rd.next_entry().await {
            if entry.path().is_dir() {
                packs.push(json!({
                    "id": entry.file_name().to_string_lossy(),
                    "path": entry.path(),
                }));
            }
        }
    }
    Json(json!({ "packs": packs }))
}

async fn install_extension() -> StatusCode {
    StatusCode::NOT_IMPLEMENTED
}

async fn delete_extension() -> StatusCode {
    StatusCode::NO_CONTENT
}

async fn ext_asset(
    Path((pack_id, path)): Path<(String, String)>,
    State(state): State<OverlayState>,
) -> Response {
    let file = state
        .paths
        .overlay_root
        .join("extensions")
        .join(&pack_id)
        .join(&path);
    serve_file(file).await
}

async fn list_assets(State(state): State<OverlayState>) -> impl IntoResponse {
    let dir = state.paths.overlay_root.join("assets");
    let mut items = vec![];
    if let Ok(mut rd) = fs::read_dir(&dir).await {
        while let Ok(Some(entry)) = rd.next_entry().await {
            items.push(json!({
                "id": entry.file_name().to_string_lossy(),
                "name": entry.file_name().to_string_lossy(),
            }));
        }
    }
    Json(json!({ "items": items }))
}

async fn upload_asset() -> StatusCode {
    StatusCode::NOT_IMPLEMENTED
}

async fn delete_asset() -> StatusCode {
    StatusCode::NO_CONTENT
}

async fn obs_video_settings() -> impl IntoResponse {
    Json(json!({
        "baseWidth": 1920,
        "baseHeight": 1080,
        "outputWidth": 1920,
        "outputHeight": 1080,
        "fpsNumerator": 60,
        "fpsDenominator": 1
    }))
}

#[derive(Deserialize)]
struct PreviewQuery {
    #[serde(default)]
    _source: Option<String>,
}

async fn obs_preview(Query(_q): Query<PreviewQuery>) -> StatusCode {
    StatusCode::NO_CONTENT
}

async fn chat_index() -> impl IntoResponse {
    Html("<!doctype html><title>Chat</title>")
}

async fn chat_config(State(state): State<OverlayState>) -> impl IntoResponse {
    let settings = state.settings.load().await.unwrap_or_default();
    Json(json!({
        "enabled": settings.overlay.chat.enabled,
        "enableBttv": settings.overlay.chat.enable_bttv,
        "enableFfz": settings.overlay.chat.enable_ffz,
        "enableSevenTv": settings.overlay.chat.enable_seven_tv,
        "showTwitchEvents": settings.overlay.chat.show_twitch_events,
        "maxBufferedMessages": settings.overlay.chat.max_buffered_messages,
    }))
}

async fn chat_history(State(state): State<OverlayState>) -> Response {
    let path = state.paths.overlay_root.join("chat-history.json");
    match fs::read(&path).await {
        Ok(bytes) => (
            [(header::CONTENT_TYPE, HeaderValue::from_static("application/json"))],
            bytes,
        )
            .into_response(),
        Err(_) => Json(json!({ "messages": [] })).into_response(),
    }
}

async fn chat_background() -> StatusCode {
    StatusCode::NO_CONTENT
}

async fn serve_file(path: std::path::PathBuf) -> Response {
    match fs::read(&path).await {
        Ok(bytes) => {
            let mime = mime_guess::from_path(&path).first_or_octet_stream();
            (
                [(
                    header::CONTENT_TYPE,
                    HeaderValue::from_str(mime.essence_str())
                        .unwrap_or(HeaderValue::from_static("application/octet-stream")),
                )],
                bytes,
            )
                .into_response()
        }
        Err(_) => StatusCode::NOT_FOUND.into_response(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::hub::RealtimeHub;
    use crate::{router_for_tests, OverlayState};
    use axum::body::Body;
    use axum::http::Request;
    use ccs_core::{AppPaths, JsonSettingsStore};
    use http_body_util::BodyExt;
    use std::sync::Arc;
    use tempfile::tempdir;
    use tokio::sync::RwLock;
    use tower::ServiceExt;

    async fn test_state() -> OverlayState {
        let dir = tempdir().unwrap();
        let root = dir.path().join("CreatorControlSuite");
        let paths = AppPaths::from_root(root.clone());
        paths.ensure_dirs().unwrap();
        let settings = Arc::new(JsonSettingsStore::new(paths.settings_file.clone()));
        let _ = settings.load().await.unwrap();
        std::mem::forget(dir);
        OverlayState {
            overlay_data: paths.overlay_root.join("overlay-data.json"),
            settings,
            paths,
            hub: Arc::new(RealtimeHub::new()),
            clients: Arc::new(RwLock::new(0)),
        }
    }

    #[tokio::test]
    async fn health_ok() {
        let state = test_state().await;
        let app = router_for_tests(state);
        let res = app
            .oneshot(Request::builder().uri("/health").body(Body::empty()).unwrap())
            .await
            .unwrap();
        assert_eq!(res.status(), StatusCode::OK);
        let bytes = res.into_body().collect().await.unwrap().to_bytes();
        let json: Value = serde_json::from_slice(&bytes).unwrap();
        assert_eq!(json["ok"], true);
        assert!(json["canvases"].as_array().unwrap().len() >= 1);
    }

    #[tokio::test]
    async fn layout_roundtrip() {
        let state = test_state().await;
        let body = json!({ "id": "default", "width": 1920, "height": 1080, "items": [] });
        let put = router_for_tests(state.clone())
            .oneshot(
                Request::builder()
                    .method("PUT")
                    .uri("/layout/default")
                    .header("content-type", "application/json")
                    .body(Body::from(body.to_string()))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(put.status(), StatusCode::NO_CONTENT);

        let get = router_for_tests(state)
            .oneshot(Request::builder().uri("/layout/default").body(Body::empty()).unwrap())
            .await
            .unwrap();
        assert_eq!(get.status(), StatusCode::OK);
    }

    #[tokio::test]
    async fn editor_and_view_instance_routes_serve_html() {
        let state = test_state().await;
        for uri in ["/editor/my-canvas", "/view/my-canvas"] {
            let app = router_for_tests(state.clone());
            let res = app
                .oneshot(
                    Request::builder()
                        .uri(uri)
                        .body(Body::empty())
                        .unwrap(),
                )
                .await
                .unwrap();
            assert_eq!(res.status(), StatusCode::OK, "{uri}");
            let bytes = res.into_body().collect().await.unwrap().to_bytes();
            let html = String::from_utf8(bytes.to_vec()).unwrap();
            assert!(
                html.contains("<!DOCTYPE html") || html.contains("<html"),
                "{uri}: {html}"
            );
        }
    }
}
