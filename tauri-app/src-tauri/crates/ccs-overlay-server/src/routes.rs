use crate::{assets, MediaLibrary};

use crate::layout_store::OverlayLayoutStore;

use crate::state::OverlayState;

use axum::extract::ws::WebSocketUpgrade;

use axum::extract::{DefaultBodyLimit, Multipart, Path, State};

use axum::http::{header, HeaderValue, StatusCode};

use axum::response::{Html, IntoResponse, Response};

use axum::routing::{delete, get, post};

use axum::{Json, Router};

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
        .route(
            "/editor",
            get(|State(state): State<OverlayState>| redirect_selected(state, "editor")),
        )
        .route(
            "/view",
            get(|State(state): State<OverlayState>| redirect_selected(state, "view")),
        )
        .route(
            "/editor/{instance_id}",
            get(|Path(_id): Path<String>| html_kind("editor")),
        )
        .route(
            "/view/{instance_id}",
            get(|Path(_id): Path<String>| html_kind("view")),
        )
        .route("/w/{ty}", get(|Path(_ty): Path<String>| html_kind("solo")))
        .route(
            "/w/shape/{*shape_id}",
            get(|Path(_id): Path<String>| html_kind("solo")),
        )
        .route("/extensions", get(list_extensions))
        .route("/extensions/install", post(install_extension))
        .route("/extensions/{pack_id}", delete(delete_extension))
        .route("/ext/{pack_id}/{*path}", get(ext_asset))
        .route("/assets", get(list_assets).post(upload_asset))
        .route("/assets/{id}", get(get_asset).delete(delete_asset))
        .route("/obs/video-settings", get(obs_video_settings))
        .route("/obs/preview", get(obs_preview))
        .route("/chat", get(chat_index))
        .route("/chat/{file}", get(chat_asset))
        .route("/chat/config", get(chat_config))
        .route("/chat/history", get(chat_history))
        .route("/chat/background", get(chat_background))
        .layer(DefaultBodyLimit::max(51 * 1024 * 1024))
        .layer(axum::middleware::from_fn(write_origin))
        .with_state(state)
        .layer(
            CorsLayer::new()
                .allow_origin(Any)
                .allow_methods(Any)
                .allow_headers(Any),
        )
}

async fn write_origin(request: axum::extract::Request, next: axum::middleware::Next) -> Response {
    if request.method() != axum::http::Method::GET
        && request.method() != axum::http::Method::HEAD
        && request.method() != axum::http::Method::OPTIONS
        || request.uri().path() == "/ws"
    {
        if let Some(origin) = request.headers().get(header::ORIGIN) {
            let allowed = origin
                .to_str()
                .ok()
                .and_then(|value| value.parse::<axum::http::Uri>().ok())
                .is_some_and(|uri| {
                    matches!(
                        uri.host(),
                        Some("127.0.0.1" | "localhost" | "[::1]" | "tauri.localhost")
                    )
                });
            if !allowed {
                return StatusCode::FORBIDDEN.into_response();
            }
        }
    }
    let dynamic = ["/data/", "/layout/", "/chat/", "/obs/", "/health"]
        .iter()
        .any(|prefix| request.uri().path().starts_with(prefix));
    let mut response = next.run(request).await;
    if dynamic {
        response.headers_mut().insert(
            header::CACHE_CONTROL,
            HeaderValue::from_static("no-store, no-cache, must-revalidate"),
        );
    }
    response
}

async fn health(State(state): State<OverlayState>) -> impl IntoResponse {
    let mut settings = state.settings.load().await.unwrap_or_default();
    settings.overlay.web_server_port = state.port;

    settings.overlay.ensure_canvases_migrated();

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
        .chain(assets::list_shape_types().into_iter().map(
            |t| json!({ "type": t, "url": settings.overlay.widget_url(&format!("shape/{t}")) }),
        ))
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

async fn ws_upgrade(ws: WebSocketUpgrade, State(state): State<OverlayState>) -> impl IntoResponse {
    ws.on_upgrade(move |socket| async move {
        let canvases = state
            .settings
            .load()
            .await
            .map(|s| s.overlay.canvases)
            .unwrap_or_default();
        state
            .hub
            .handle_socket(
                socket,
                OverlayLayoutStore::new(state.paths.overlay_layouts),
                canvases,
                state.shutdown,
            )
            .await;
    })
}

async fn get_layout(
    Path(instance_id): Path<String>,

    State(state): State<OverlayState>,
) -> Response {
    let store = OverlayLayoutStore::new(&state.paths.overlay_layouts);

    match store.load(&instance_id).await {
        Ok(layout) => Json(layout).into_response(),
        Err(crate::layout_store::LayoutError::InvalidId) => {
            api_error("invalid instance id").into_response()
        }
        Err(error) => (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({"error":error.to_string()})),
        )
            .into_response(),
    }
}

async fn put_layout(
    Path(instance_id): Path<String>,

    State(state): State<OverlayState>,

    payload: Result<Json<Value>, axum::extract::rejection::JsonRejection>,
) -> Result<impl IntoResponse, ApiError> {
    let Json(body) = payload.map_err(|_| api_error("invalid json"))?;
    if !body.is_object() {
        return Err(api_error("invalid layout"));
    }
    let store = OverlayLayoutStore::new(&state.paths.overlay_layouts);

    store.save(&instance_id, &body).await.map_err(api_error)?;

    state.hub.publish(&json!({

        "source": "app",

        "type": "app.overlay.layout",

        "at": chrono::Utc::now().to_rfc3339(),

        "summary": "Layout gespeichert",

        "data": {"instanceId": instance_id, "layout": body.to_string()},

    }));

    Ok(Json(body))
}

async fn overlay_data(State(state): State<OverlayState>) -> Json<Value> {
    Json(state.hub.live.data.read().unwrap().clone())
}
async fn overlay_config(State(state): State<OverlayState>) -> Json<Value> {
    let settings = match state.settings.load().await {
        Ok(s) => s,
        Err(_) => return Json(json!({})),
    };
    let path = ccs_core::paths::overlay_data_path(&state.paths, &settings)
        .with_file_name("overlay-config.json");
    Json(
        fs::read(path)
            .await
            .ok()
            .and_then(|bytes| serde_json::from_slice(&bytes).ok())
            .unwrap_or_else(|| json!({})),
    )
}

async fn size_presets() -> Json<Value> {
    Json(
        json!([{"id": "1080p", "label": "1920 × 1080 (Full HD)", "width": 1920, "height": 1080}, {"id": "720p", "label": "1280 × 720 (HD)", "width": 1280, "height": 720}, {"id": "1440p", "label": "2560 × 1440 (QHD)", "width": 2560, "height": 1440}, {"id": "4k", "label": "3840 × 2160 (4K)", "width": 3840, "height": 2160}, {"id": "1080p-vert", "label": "1080 × 1920 (Vertical)", "width": 1080, "height": 1920}, {"id": "720p-vert", "label": "720 × 1280 (Vertical)", "width": 720, "height": 1280}, {"id": "square", "label": "1080 × 1080 (Square)", "width": 1080, "height": 1080}]),
    )
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

type ApiError = (StatusCode, Json<Value>);

fn api_error(message: impl ToString) -> ApiError {
    (
        StatusCode::BAD_REQUEST,
        Json(json!({"error": message.to_string()})),
    )
}

async fn library_call<T: Send + 'static>(
    state: OverlayState,
    call: impl FnOnce(MediaLibrary) -> Result<T, String> + Send + 'static,
) -> Result<T, ApiError> {
    tokio::task::spawn_blocking(move || call(MediaLibrary::new(state.paths.overlay_root)))
        .await
        .map_err(api_error)?
        .map_err(api_error)
}

async fn list_extensions(State(state): State<OverlayState>) -> Result<Json<Value>, ApiError> {
    Ok(Json(
        json!({"packs":library_call(state, |lib| lib.packs()).await?}),
    ))
}

async fn uploaded_file(mut multipart: Multipart) -> Result<(String, Vec<u8>), ApiError> {
    while let Some(field) = multipart.next_field().await.map_err(api_error)? {
        if let Some(name) = field.file_name().map(str::to_string) {
            let bytes = field.bytes().await.map_err(api_error)?;

            return Ok((name, bytes.to_vec()));
        }
    }

    Err(api_error("Keine Datei übermittelt"))
}

async fn install_extension(
    State(state): State<OverlayState>,
    multipart: Multipart,
) -> Result<Json<Value>, ApiError> {
    let (_, bytes) = uploaded_file(multipart).await?;

    Ok(Json(
        library_call(state, move |lib| lib.install_pack(&bytes)).await?,
    ))
}

async fn delete_extension(
    Path(id): Path<String>,
    State(state): State<OverlayState>,
) -> Result<Json<Value>, ApiError> {
    library_call(state, move |lib| lib.delete_pack(&id)).await?;

    Ok(Json(json!({"ok":true})))
}

async fn ext_asset(
    Path((id, path)): Path<(String, String)>,
    State(state): State<OverlayState>,
) -> Response {
    match MediaLibrary::new(state.paths.overlay_root).extension_path(&id, &path) {
        Ok(path) => serve_file(path).await,

        Err(_) => StatusCode::NOT_FOUND.into_response(),
    }
}

async fn list_assets(State(state): State<OverlayState>) -> Result<Json<Value>, ApiError> {
    Ok(Json(
        json!({"assets":library_call(state, |lib| lib.assets()).await?}),
    ))
}

async fn upload_asset(
    State(state): State<OverlayState>,
    multipart: Multipart,
) -> Result<Json<Value>, ApiError> {
    let (name, bytes) = uploaded_file(multipart).await?;

    Ok(Json(
        library_call(state, move |lib| lib.import_image(&name, &bytes)).await?,
    ))
}

async fn delete_asset(
    Path(id): Path<String>,
    State(state): State<OverlayState>,
) -> Result<Json<Value>, ApiError> {
    library_call(state, move |lib| lib.delete_asset(&id)).await?;

    Ok(Json(json!({"ok":true})))
}

async fn get_asset(Path(id): Path<String>, State(state): State<OverlayState>) -> Response {
    match MediaLibrary::new(state.paths.overlay_root).asset_path(&id) {
        Ok(path) => serve_file(path).await,

        Err(_) => StatusCode::NOT_FOUND.into_response(),
    }
}

async fn obs_video_settings(State(state): State<OverlayState>) -> Json<Value> {
    let provider = state.hub.obs.read().unwrap().clone();
    if let Some(provider) = provider {
        if let Ok(value) = provider.video_settings().await {
            return Json(value);
        }
    }
    Json(json!({"connected":false,"baseWidth":0,"baseHeight":0,"outputWidth":0,"outputHeight":0}))
}
async fn obs_preview(State(state): State<OverlayState>) -> Response {
    let provider = state.hub.obs.read().unwrap().clone();
    if let Some(provider) = provider {
        if let Ok(bytes) = provider.preview().await {
            return (
                [
                    (header::CONTENT_TYPE, "image/png"),
                    (header::CACHE_CONTROL, "no-store"),
                ],
                bytes,
            )
                .into_response();
        }
    }
    StatusCode::SERVICE_UNAVAILABLE.into_response()
}

const CHAT_HTML: &str = include_str!(concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../../../../src/CreatorControlSuite.Modules.Overlay/ChatOverlay/index.html"
));
async fn chat_index() -> Html<&'static str> {
    Html(CHAT_HTML)
}
async fn chat_asset(Path(file): Path<String>) -> Response {
    match file.as_str() {
        "chat.js" => (
            [(header::CONTENT_TYPE, "application/javascript")],
            include_str!(concat!(
                env!("CARGO_MANIFEST_DIR"),
                "/../../../../src/CreatorControlSuite.Modules.Overlay/ChatOverlay/chat.js"
            )),
        )
            .into_response(),
        "chat.css" => (
            [(header::CONTENT_TYPE, "text/css")],
            include_str!(concat!(
                env!("CARGO_MANIFEST_DIR"),
                "/../../../../src/CreatorControlSuite.Modules.Overlay/ChatOverlay/chat.css"
            )),
        )
            .into_response(),
        _ => StatusCode::NOT_FOUND.into_response(),
    }
}
async fn chat_config(State(state): State<OverlayState>) -> Result<Json<Value>, ApiError> {
    let settings = state.settings.load().await.map_err(api_error)?;
    let raw = serde_json::to_value(settings.overlay.chat).map_err(api_error)?;
    let mut config = json!({"backgroundType":"None","backgroundColor":"#000000","backgroundOpacity":0.55,"paddingPx":12,"borderRadiusPx":12,"gapPx":6,"fontSizePx":18,"fontFamily":"Segoe UI, system-ui, sans-serif"});
    for (key, value) in raw
        .as_object()
        .ok_or_else(|| api_error("Chat-Konfiguration ungültig"))?
    {
        if key == "BackgroundImagePath" {
            continue;
        }
        let camel = format!("{}{}", key[..1].to_ascii_lowercase(), &key[1..]);
        config[camel] = value.clone();
    }
    for (key, min, max) in [
        ("backgroundOpacity", 0.0, 1.0),
        ("paddingPx", 0.0, 120.0),
        ("borderRadiusPx", 0.0, 64.0),
        ("gapPx", 0.0, 48.0),
        ("fontSizePx", 8.0, 72.0),
    ] {
        if let Some(value) = config[key].as_f64() {
            config[key] = if key == "backgroundOpacity" {
                json!(value.clamp(min, max))
            } else {
                json!(value.clamp(min, max) as i64)
            };
        }
    }
    let kind = config["backgroundType"]
        .as_str()
        .unwrap_or("None")
        .trim()
        .to_ascii_lowercase();
    let image = raw["BackgroundImagePath"]
        .as_str()
        .map(ccs_core::paths::expand_path)
        .filter(|p| p.is_file());
    config["backgroundType"] = json!(match kind.as_str() {
        "image" if image.is_some() => "Image",
        "color" => "Color",
        _ => "None",
    });
    config["backgroundVersion"] = json!(image
        .and_then(|p| std::fs::metadata(p).ok())
        .and_then(|m| m.modified().ok())
        .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|d| d.as_nanos().to_string())
        .unwrap_or_else(|| "0".into()));
    Ok(Json(config))
}
async fn chat_history(State(state): State<OverlayState>) -> Json<Value> {
    let config = state.settings.load().await.ok().map(|s| s.overlay.chat);
    let mut history = state.hub.history();
    let limit = config
        .as_ref()
        .filter(|c| c.enabled)
        .map(|c| c.max_buffered_messages.clamp(0, 2000) as usize)
        .unwrap_or(0);
    if let Some(events) = history["events"].as_array_mut() {
        let excess = events.len().saturating_sub(limit);
        events.drain(..excess);
    }
    Json(history)
}
async fn chat_background(State(state): State<OverlayState>) -> Response {
    if let Ok(settings) = state.settings.load().await {
        if let Some(path) = settings
            .overlay
            .chat
            .extra
            .get("BackgroundImagePath")
            .and_then(Value::as_str)
        {
            if !path.is_empty() {
                return serve_file(ccs_core::paths::expand_path(path)).await;
            }
        }
    }
    StatusCode::NOT_FOUND.into_response()
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
            port: 8765,
            shutdown: None,
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
            .oneshot(
                Request::builder()
                    .uri("/health")
                    .body(Body::empty())
                    .unwrap(),
            )
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

        assert_eq!(put.status(), StatusCode::OK);

        let get = router_for_tests(state)
            .oneshot(
                Request::builder()
                    .uri("/layout/default")
                    .body(Body::empty())
                    .unwrap(),
            )
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
                .oneshot(Request::builder().uri(uri).body(Body::empty()).unwrap())
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

async fn redirect_selected(state: OverlayState, kind: &str) -> Response {
    match state.settings.load().await {
        Ok(settings) => {
            let id = settings.overlay.selected_canvas().id;
            (
                StatusCode::FOUND,
                [(header::LOCATION, format!("/{kind}/{id}"))],
            )
                .into_response()
        }
        Err(error) => api_error(error).into_response(),
    }
}
