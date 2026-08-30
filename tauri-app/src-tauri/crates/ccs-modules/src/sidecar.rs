//! Optional .NET sidecar client for modules not yet in Rust.

use crate::{ConnectionState, ModuleError, ModuleResult, ServiceStatus};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::time::Duration;
use tokio::process::Child;
use tokio::sync::{Mutex, RwLock};

pub const DEFAULT_SIDECAR_PORT: u16 = 18765;
pub const SIDECAR_ENABLE_ENV: &str = "CCS_SIDECAR";
pub const SIDECAR_BINARY_NAME: &str = if cfg!(windows) {
    "CreatorControlSuite.CommandClient.exe"
} else {
    "CreatorControlSuite.CommandClient"
};

const HEALTH_POLL_INTERVAL: Duration = Duration::from_millis(100);
const HEALTH_WAIT: Duration = Duration::from_secs(5);
const HTTP_TIMEOUT: Duration = Duration::from_secs(2);

#[derive(Debug, Clone)]
pub struct SidecarConfig {
    pub base_url: String,
    pub port: u16,
}

impl Default for SidecarConfig {
    fn default() -> Self {
        Self::from_port(DEFAULT_SIDECAR_PORT)
    }
}

impl SidecarConfig {
    pub fn from_port(port: u16) -> Self {
        Self {
            base_url: format!("http://127.0.0.1:{port}"),
            port,
        }
    }

    pub fn from_base_url(base_url: impl Into<String>) -> Self {
        let base_url = base_url.into();
        Self {
            base_url,
            port: DEFAULT_SIDECAR_PORT,
        }
    }

    fn origin(&self) -> &str {
        self.base_url.trim_end_matches('/')
    }

    pub fn health_url(&self) -> String {
        format!("{}/sidecar/health", self.origin())
    }

    pub fn workflow_run_url(&self) -> String {
        format!("{}/sidecar/workflow/run", self.origin())
    }

    pub fn ytm_now_playing_url(&self) -> String {
        format!("{}/sidecar/ytm/now-playing", self.origin())
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct SidecarHealth {
    #[serde(default)]
    pub ok: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct YtmNowPlaying {
    #[serde(default = "ytmusic_provider")]
    pub provider: String,
    #[serde(default)]
    pub connected: bool,
    #[serde(default)]
    pub is_playing: bool,
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub status_text: String,
}

fn ytmusic_provider() -> String {
    "ytmusic".into()
}

impl Default for YtmNowPlaying {
    fn default() -> Self {
        Self {
            provider: ytmusic_provider(),
            connected: false,
            is_playing: false,
            title: String::new(),
            artist: String::new(),
            album: String::new(),
            status_text: "Nicht verbunden".into(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct WorkflowRunRequest {
    pub command: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct WorkflowRunResponse {
    #[serde(default)]
    pub ok: bool,
    #[serde(default)]
    pub message: String,
}

pub const WORKFLOW_PREPARE: &str = "workflow.prepare";
pub const SIDECAR_NOT_CONNECTED: &str = "Sidecar nicht verbunden";

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SidecarLaunchDecision {
    Spawn,
    Skip(String),
    Error(String),
}

#[derive(Debug, Clone)]
pub struct SidecarLaunchInput {
    pub enabled: bool,
    pub binary_exists: bool,
    pub overlay_port_busy: bool,
    pub sidecar_port_busy: bool,
    pub is_windows: bool,
    pub overlay_port: u16,
    pub sidecar_port: u16,
}

pub fn overlay_port_busy_message(port: u16) -> String {
    format!("Overlay-Port {port} ist belegt")
}

pub fn sidecar_port_busy_message(port: u16) -> String {
    format!("Sidecar-Port {port} ist belegt")
}

pub fn env_enabled() -> bool {
    match std::env::var(SIDECAR_ENABLE_ENV) {
        Ok(value) => matches!(
            value.trim(),
            "1" | "true" | "TRUE" | "yes" | "YES" | "on" | "ON"
        ),
        Err(_) => false,
    }
}

pub fn decide_launch(input: &SidecarLaunchInput) -> SidecarLaunchDecision {
    if !input.is_windows {
        return SidecarLaunchDecision::Skip("Sidecar unter macOS nicht verfügbar".into());
    }
    if !input.enabled {
        return SidecarLaunchDecision::Skip(String::new());
    }
    if !input.binary_exists {
        return SidecarLaunchDecision::Skip(String::new());
    }
    if input.overlay_port_busy {
        return SidecarLaunchDecision::Error(overlay_port_busy_message(input.overlay_port));
    }
    if input.sidecar_port_busy {
        return SidecarLaunchDecision::Error(sidecar_port_busy_message(input.sidecar_port));
    }
    SidecarLaunchDecision::Spawn
}

pub async fn port_is_occupied(port: u16) -> bool {
    tokio::net::TcpStream::connect(("127.0.0.1", port))
        .await
        .is_ok()
}

pub fn resolve_binary(explicit: Option<&Path>, search_dirs: &[PathBuf]) -> Option<PathBuf> {
    if let Some(path) = explicit {
        if !path.as_os_str().is_empty() && path.is_file() {
            return Some(path.to_path_buf());
        }
    }
    for dir in search_dirs {
        let candidate = dir.join(SIDECAR_BINARY_NAME);
        if candidate.is_file() {
            return Some(candidate);
        }
    }
    None
}

pub struct SidecarClient {
    http: reqwest::Client,
    config: SidecarConfig,
}

impl SidecarClient {
    pub fn new(config: SidecarConfig) -> Self {
        let http = reqwest::Client::builder()
            .timeout(HTTP_TIMEOUT)
            .build()
            .unwrap_or_else(|_| reqwest::Client::new());
        Self { http, config }
    }

    pub fn config(&self) -> &SidecarConfig {
        &self.config
    }

    pub async fn health(&self) -> ModuleResult<SidecarHealth> {
        let response = self.http.get(self.config.health_url()).send().await?;
        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(ModuleError::Message(format!(
                "Sidecar-Health HTTP {}: {body}",
                status.as_u16()
            )));
        }
        let parsed: SidecarHealth = serde_json::from_str(&body)
            .map_err(|e| ModuleError::Message(format!("Sidecar-Health ungültig: {e}")))?;
        Ok(parsed)
    }

    pub async fn ytm_now_playing(&self) -> ModuleResult<YtmNowPlaying> {
        let response = self
            .http
            .get(self.config.ytm_now_playing_url())
            .send()
            .await?;
        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(ModuleError::Message(format!(
                "Sidecar YTM HTTP {}: {body}",
                status.as_u16()
            )));
        }
        let parsed: YtmNowPlaying = serde_json::from_str(&body)
            .map_err(|e| ModuleError::Message(format!("Sidecar-YTM ungültig: {e}")))?;
        Ok(parsed)
    }

    pub async fn run_workflow(&self, command: &str) -> ModuleResult<WorkflowRunResponse> {
        let response = self
            .http
            .post(self.config.workflow_run_url())
            .json(&WorkflowRunRequest {
                command: command.into(),
            })
            .send()
            .await?;
        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(ModuleError::Message(format!(
                "Sidecar-Workflow HTTP {}: {body}",
                status.as_u16()
            )));
        }
        let parsed: WorkflowRunResponse = serde_json::from_str(&body)
            .map_err(|e| ModuleError::Message(format!("Sidecar-Workflow ungültig: {e}")))?;
        Ok(parsed)
    }
}

pub async fn wait_for_health(
    client: &SidecarClient,
    timeout: Duration,
) -> ModuleResult<SidecarHealth> {
    let deadline = tokio::time::Instant::now() + timeout;
    loop {
        match client.health().await {
            Ok(health) if health.ok => return Ok(health),
            Ok(_) => {}
            Err(err) if tokio::time::Instant::now() >= deadline => return Err(err),
            Err(_) => {}
        }
        if tokio::time::Instant::now() >= deadline {
            return Err(ModuleError::Message(
                "Sidecar-Health-Timeout: keine Antwort".into(),
            ));
        }
        tokio::time::sleep(HEALTH_POLL_INTERVAL).await;
    }
}

#[derive(Debug, Clone)]
pub struct SidecarStartOptions {
    pub enabled: bool,
    pub port: u16,
    pub overlay_failed: bool,
    pub overlay_port: u16,
    pub binary: Option<PathBuf>,
    pub is_windows: bool,
}

impl Default for SidecarStartOptions {
    fn default() -> Self {
        Self {
            enabled: false,
            port: DEFAULT_SIDECAR_PORT,
            overlay_failed: false,
            overlay_port: 8765,
            binary: None,
            is_windows: cfg!(windows),
        }
    }
}

pub struct SidecarSupervisor {
    status: RwLock<ServiceStatus>,
    child: Mutex<Option<Child>>,
    client: RwLock<SidecarClient>,
}

impl SidecarSupervisor {
    pub fn new() -> Self {
        Self {
            status: RwLock::new(sidecar_status(ConnectionState::Disconnected, "")),
            child: Mutex::new(None),
            client: RwLock::new(SidecarClient::new(SidecarConfig::default())),
        }
    }

    pub fn new_shared() -> std::sync::Arc<Self> {
        std::sync::Arc::new(Self::new())
    }

    pub async fn status(&self) -> ServiceStatus {
        self.status.read().await.clone()
    }

    pub async fn ytm_now_playing(&self) -> YtmNowPlaying {
        if self.status.read().await.state != ConnectionState::Connected {
            return YtmNowPlaying::default();
        }
        match self.client.read().await.ytm_now_playing().await {
            Ok(playing) => playing,
            Err(_) => YtmNowPlaying::default(),
        }
    }

    pub async fn run_workflow(&self, command: &str) -> WorkflowRunResponse {
        if self.status.read().await.state != ConnectionState::Connected {
            return WorkflowRunResponse {
                ok: false,
                message: SIDECAR_NOT_CONNECTED.into(),
            };
        }
        let command = if command.trim().is_empty() {
            WORKFLOW_PREPARE
        } else {
            command
        };
        match self.client.read().await.run_workflow(command).await {
            Ok(response) => response,
            Err(err) => WorkflowRunResponse {
                ok: false,
                message: err.to_string(),
            },
        }
    }

    pub async fn start(&self, options: SidecarStartOptions) -> ServiceStatus {
        let _ = self.stop_child().await;
        *self.client.write().await = SidecarClient::new(SidecarConfig::from_port(options.port));

        let sidecar_port_busy = port_is_occupied(options.port).await;
        let decision = decide_launch(&SidecarLaunchInput {
            enabled: options.enabled,
            binary_exists: options.binary.is_some(),
            overlay_port_busy: options.overlay_failed,
            sidecar_port_busy,
            is_windows: options.is_windows,
            overlay_port: options.overlay_port,
            sidecar_port: options.port,
        });

        match decision {
            SidecarLaunchDecision::Skip(detail) => {
                self.set_status(ConnectionState::Disconnected, detail).await
            }
            SidecarLaunchDecision::Error(detail) => {
                self.set_status(ConnectionState::Error, detail).await
            }
            SidecarLaunchDecision::Spawn => self.spawn(options).await,
        }
    }

    async fn spawn(&self, options: SidecarStartOptions) -> ServiceStatus {
        let Some(binary) = options.binary else {
            return self.set_status(ConnectionState::Disconnected, "").await;
        };

        self.set_status(ConnectionState::Connecting, "").await;

        let mut command = tokio::process::Command::new(&binary);
        command
            .arg("--sidecar")
            .arg("--port")
            .arg(options.port.to_string())
            .kill_on_drop(true)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null());
        #[cfg(windows)]
        {
            const CREATE_NO_WINDOW: u32 = 0x0800_0000;
            command.creation_flags(CREATE_NO_WINDOW);
        }

        match command.spawn() {
            Ok(child) => {
                *self.child.lock().await = Some(child);
            }
            Err(err) => {
                return self
                    .set_status(
                        ConnectionState::Error,
                        format!("Sidecar konnte nicht gestartet werden: {err}"),
                    )
                    .await;
            }
        }

        let client = self.client.read().await;
        match wait_for_health(&client, HEALTH_WAIT).await {
            Ok(_) => {
                drop(client);
                self.set_status(
                    ConnectionState::Connected,
                    format!("http://127.0.0.1:{}", options.port),
                )
                .await
            }
            Err(err) => {
                drop(client);
                let _ = self.stop_child().await;
                self.set_status(ConnectionState::Error, err.to_string())
                    .await
            }
        }
    }

    async fn stop_child(&self) -> ModuleResult<()> {
        if let Some(mut child) = self.child.lock().await.take() {
            let _ = child.kill().await;
        }
        Ok(())
    }

    async fn set_status(&self, state: ConnectionState, detail: impl Into<String>) -> ServiceStatus {
        let next = sidecar_status(state, detail);
        *self.status.write().await = next.clone();
        next
    }
}

impl Default for SidecarSupervisor {
    fn default() -> Self {
        Self::new()
    }
}

fn sidecar_status(state: ConnectionState, detail: impl Into<String>) -> ServiceStatus {
    ServiceStatus {
        id: "sidecar".into(),
        name: "Sidecar".into(),
        state,
        detail: detail.into(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    use wiremock::matchers::{body_string_contains, method, path};
    use wiremock::{Mock, MockServer, ResponseTemplate};

    fn spawn_input() -> SidecarLaunchInput {
        SidecarLaunchInput {
            enabled: true,
            binary_exists: true,
            overlay_port_busy: false,
            sidecar_port_busy: false,
            is_windows: true,
            overlay_port: 8765,
            sidecar_port: DEFAULT_SIDECAR_PORT,
        }
    }

    #[test]
    fn health_url() {
        assert_eq!(
            SidecarConfig::default().health_url(),
            "http://127.0.0.1:18765/sidecar/health"
        );
    }

    #[test]
    fn url_contract_trims_trailing_slash() {
        let config = SidecarConfig::from_base_url("http://127.0.0.1:18765/");
        assert_eq!(config.health_url(), "http://127.0.0.1:18765/sidecar/health");
        assert_eq!(
            config.workflow_run_url(),
            "http://127.0.0.1:18765/sidecar/workflow/run"
        );
        assert_eq!(
            config.ytm_now_playing_url(),
            "http://127.0.0.1:18765/sidecar/ytm/now-playing"
        );
    }

    #[test]
    fn from_port_sets_loopback_base() {
        let config = SidecarConfig::from_port(19001);
        assert_eq!(config.base_url, "http://127.0.0.1:19001");
        assert_eq!(config.health_url(), "http://127.0.0.1:19001/sidecar/health");
    }

    #[test]
    fn decide_skip_when_disabled() {
        let mut input = spawn_input();
        input.enabled = false;
        assert_eq!(
            decide_launch(&input),
            SidecarLaunchDecision::Skip(String::new())
        );
    }

    #[test]
    fn decide_skip_when_binary_missing() {
        let mut input = spawn_input();
        input.binary_exists = false;
        assert_eq!(
            decide_launch(&input),
            SidecarLaunchDecision::Skip(String::new())
        );
    }

    #[test]
    fn decide_skip_on_macos() {
        let mut input = spawn_input();
        input.is_windows = false;
        assert_eq!(
            decide_launch(&input),
            SidecarLaunchDecision::Skip("Sidecar unter macOS nicht verfügbar".into())
        );
    }

    #[test]
    fn decide_error_when_overlay_port_busy() {
        let mut input = spawn_input();
        input.overlay_port_busy = true;
        assert_eq!(
            decide_launch(&input),
            SidecarLaunchDecision::Error("Overlay-Port 8765 ist belegt".into())
        );
    }

    #[test]
    fn decide_error_when_sidecar_port_busy() {
        let mut input = spawn_input();
        input.sidecar_port_busy = true;
        assert_eq!(
            decide_launch(&input),
            SidecarLaunchDecision::Error("Sidecar-Port 18765 ist belegt".into())
        );
    }

    #[test]
    fn decide_spawn_when_ready() {
        assert_eq!(decide_launch(&spawn_input()), SidecarLaunchDecision::Spawn);
    }

    #[test]
    fn resolve_binary_prefers_explicit_file() {
        let dir = tempfile::tempdir().unwrap();
        let explicit = dir.path().join("custom-sidecar.exe");
        std::fs::write(&explicit, b"x").unwrap();
        let found = resolve_binary(Some(&explicit), &[]).unwrap();
        assert_eq!(found, explicit);
    }

    #[test]
    fn resolve_binary_searches_exe_dir() {
        let dir = tempfile::tempdir().unwrap();
        let candidate = dir.path().join(SIDECAR_BINARY_NAME);
        std::fs::write(&candidate, b"x").unwrap();
        let found = resolve_binary(None, &[dir.path().to_path_buf()]).unwrap();
        assert_eq!(found, candidate);
    }

    #[test]
    fn resolve_binary_none_when_missing() {
        let dir = tempfile::tempdir().unwrap();
        assert!(resolve_binary(None, &[dir.path().to_path_buf()]).is_none());
        assert!(resolve_binary(Some(Path::new("")), &[]).is_none());
    }

    #[tokio::test]
    async fn port_is_occupied_when_listener_bound() {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        assert!(port_is_occupied(port).await);
        drop(listener);
        assert!(!port_is_occupied(port).await);
    }

    #[tokio::test]
    async fn health_ok_against_wiremock() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/sidecar/health"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({ "ok": true })))
            .mount(&server)
            .await;

        let client = SidecarClient::new(SidecarConfig::from_base_url(server.uri()));
        let health = client.health().await.unwrap();
        assert!(health.ok);
    }

    #[tokio::test]
    async fn ytm_now_playing_passthrough() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/sidecar/ytm/now-playing"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({
                "provider": "ytmusic",
                "connected": true,
                "isPlaying": true,
                "title": "Test Track",
                "artist": "Test Artist",
                "album": "Test Album",
                "statusText": "Spielt"
            })))
            .mount(&server)
            .await;

        let client = SidecarClient::new(SidecarConfig::from_base_url(server.uri()));
        let playing = client.ytm_now_playing().await.unwrap();
        assert!(playing.connected);
        assert!(playing.is_playing);
        assert_eq!(playing.title, "Test Track");
        assert_eq!(playing.artist, "Test Artist");
        assert_eq!(playing.album, "Test Album");
        assert_eq!(playing.status_text, "Spielt");
        assert_eq!(playing.provider, "ytmusic");
    }

    #[tokio::test]
    async fn workflow_run_posts_command() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/sidecar/workflow/run"))
            .and(body_string_contains("workflow.prepare"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({
                "ok": false,
                "message": "Run-of-Show noch nicht im Sidecar"
            })))
            .mount(&server)
            .await;

        let client = SidecarClient::new(SidecarConfig::from_base_url(server.uri()));
        let response = client.run_workflow("workflow.prepare").await.unwrap();
        assert!(!response.ok);
        assert_eq!(response.message, "Run-of-Show noch nicht im Sidecar");
    }

    #[tokio::test]
    async fn wait_for_health_times_out() {
        let server = MockServer::start().await;
        let client = SidecarClient::new(SidecarConfig::from_base_url(server.uri()));
        let err = wait_for_health(&client, Duration::from_millis(250))
            .await
            .unwrap_err();
        assert!(!err.to_string().is_empty());
    }

    #[tokio::test]
    async fn supervisor_disabled_stays_disconnected() {
        let supervisor = SidecarSupervisor::new();
        let status = supervisor.start(SidecarStartOptions::default()).await;
        assert_eq!(status.id, "sidecar");
        assert_eq!(status.state, ConnectionState::Disconnected);
        assert!(supervisor.ytm_now_playing().await.title.is_empty());
        let workflow = supervisor.run_workflow(WORKFLOW_PREPARE).await;
        assert!(!workflow.ok);
        assert_eq!(workflow.message, SIDECAR_NOT_CONNECTED);
    }

    #[tokio::test]
    async fn supervisor_run_workflow_when_disconnected_skips_http() {
        let supervisor = SidecarSupervisor::new();
        let workflow = supervisor.run_workflow("workflow.live").await;
        assert!(!workflow.ok);
        assert_eq!(workflow.message, SIDECAR_NOT_CONNECTED);
    }

    #[tokio::test]
    async fn supervisor_missing_binary_stays_disconnected() {
        let supervisor = SidecarSupervisor::new();
        let status = supervisor
            .start(SidecarStartOptions {
                enabled: true,
                is_windows: true,
                ..SidecarStartOptions::default()
            })
            .await;
        assert_eq!(status.state, ConnectionState::Disconnected);
    }

    #[tokio::test]
    async fn supervisor_overlay_port_busy_is_error() {
        let supervisor = SidecarSupervisor::new();
        let status = supervisor
            .start(SidecarStartOptions {
                enabled: true,
                overlay_failed: true,
                overlay_port: 8765,
                binary: Some(PathBuf::from(SIDECAR_BINARY_NAME)),
                is_windows: true,
                ..SidecarStartOptions::default()
            })
            .await;
        assert_eq!(status.state, ConnectionState::Error);
        assert_eq!(status.detail, "Overlay-Port 8765 ist belegt");
    }

    #[tokio::test]
    async fn supervisor_sidecar_port_busy_is_error() {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let supervisor = SidecarSupervisor::new();
        let status = supervisor
            .start(SidecarStartOptions {
                enabled: true,
                port,
                binary: Some(PathBuf::from(SIDECAR_BINARY_NAME)),
                is_windows: true,
                ..SidecarStartOptions::default()
            })
            .await;
        assert_eq!(status.state, ConnectionState::Error);
        assert_eq!(status.detail, format!("Sidecar-Port {port} ist belegt"));
    }
}
