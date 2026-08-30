mod protocol;

pub use protocol::{ObsSceneInfo, DEFAULT_EVENT_SUBSCRIPTIONS, SUPPORTED_RPC_VERSION};

use crate::{ConnectionState, ModuleError, ModuleResult, ServiceStatus};
use futures_util::{SinkExt, StreamExt};
use protocol::{
    build_request, create_identify, decode_envelope, parse_current_program_scene, parse_scene_list,
    ObsHello, ObsIdentified, ObsRequestResponse, ObsRequestStatus, EVENT_OP, HELLO_OP,
    IDENTIFIED_OP, REQUEST_RESPONSE_OP,
};
use serde_json::{json, Value};
use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;
use tokio::net::TcpStream;
use tokio::sync::{broadcast, oneshot, Mutex, RwLock};
use tokio::task::JoinHandle;
use tokio_tungstenite::{
    connect_async, tungstenite::Message, MaybeTlsStream, WebSocketStream,
};
use tracing::{debug, warn};

type WsStream = WebSocketStream<MaybeTlsStream<TcpStream>>;
type WsWriter = futures_util::stream::SplitSink<WsStream, Message>;
type WsReader = futures_util::stream::SplitStream<WsStream>;

const CONNECT_TIMEOUT: Duration = Duration::from_secs(8);
const REQUEST_TIMEOUT: Duration = Duration::from_secs(8);
const DEFAULT_RECONNECT_SECONDS: u64 = 15;

#[derive(Debug, Clone)]
pub struct ObsConnectOptions {
    pub host: String,
    pub port: u16,
    pub password: Option<String>,
    pub reconnect: bool,
    pub reconnect_seconds: u64,
}

impl ObsConnectOptions {
    pub fn websocket_url(&self) -> String {
        format!("ws://{}:{}", self.host, self.port)
    }
}

struct LiveSession {
    writer: WsWriter,
    pending: HashMap<String, oneshot::Sender<ObsRequestResponse>>,
}

/// Shared OBS WebSocket 5 client with persistent connection and optional reconnect.
pub struct ObsClient {
    status: RwLock<ServiceStatus>,
    session: Mutex<Option<LiveSession>>,
    receive_task: Mutex<Option<JoinHandle<()>>>,
    reconnect_task: Mutex<Option<JoinHandle<()>>>,
    options: RwLock<Option<ObsConnectOptions>>,
    want_connected: AtomicBool,
    allow_reconnect: AtomicBool,
    scene_tx: broadcast::Sender<String>,
    status_tx: broadcast::Sender<ServiceStatus>,
}

impl ObsClient {
    pub fn new(host: impl Into<String>, port: u16) -> Self {
        let (scene_tx, _) = broadcast::channel(16);
        let (status_tx, _) = broadcast::channel(16);
        Self {
            status: RwLock::new(ServiceStatus::disconnected("obs", "OBS")),
            session: Mutex::new(None),
            receive_task: Mutex::new(None),
            reconnect_task: Mutex::new(None),
            options: RwLock::new(Some(ObsConnectOptions {
                host: host.into(),
                port,
                password: None,
                reconnect: true,
                reconnect_seconds: DEFAULT_RECONNECT_SECONDS,
            })),
            want_connected: AtomicBool::new(false),
            allow_reconnect: AtomicBool::new(true),
            scene_tx,
            status_tx,
        }
    }

    pub fn new_shared(host: impl Into<String>, port: u16) -> Arc<Self> {
        Arc::new(Self::new(host, port))
    }

    pub async fn status(&self) -> ServiceStatus {
        self.status.read().await.clone()
    }

    pub fn subscribe_status(&self) -> broadcast::Receiver<ServiceStatus> {
        self.status_tx.subscribe()
    }

    pub fn subscribe_scenes(&self) -> broadcast::Receiver<String> {
        self.scene_tx.subscribe()
    }

    async fn publish_status(&self) {
        let snapshot = self.status.read().await.clone();
        let _ = self.status_tx.send(snapshot);
    }

    /// Identify handshake payload for OBS WebSocket 5.x (no auth). API compat.
    pub fn identify_message(rpc_version: u32) -> Value {
        json!({
            "op": 1,
            "d": {
                "rpcVersion": rpc_version,
                "eventSubscriptions": DEFAULT_EVENT_SUBSCRIPTIONS
            }
        })
    }

    /// Request payload helper (does not send).
    pub async fn set_scene(&self, scene: &str) -> Value {
        build_request(
            "SetCurrentProgramScene",
            &uuid::Uuid::new_v4().to_string(),
            Some(json!({ "sceneName": scene })),
        )
    }

    pub async fn connect(self: &Arc<Self>, options: ObsConnectOptions) -> ModuleResult<()> {
        self.teardown(false).await;
        self.allow_reconnect
            .store(options.reconnect, Ordering::SeqCst);
        self.want_connected.store(true, Ordering::SeqCst);
        *self.options.write().await = Some(options.clone());

        match self.handshake_and_start(options).await {
            Ok(()) => Ok(()),
            Err(e) => {
                if self.allow_reconnect.load(Ordering::SeqCst)
                    && self.want_connected.load(Ordering::SeqCst)
                {
                    self.schedule_reconnect();
                }
                Err(e)
            }
        }
    }

    pub async fn connect_simple(
        self: &Arc<Self>,
        host: impl Into<String>,
        port: u16,
        password: Option<String>,
        reconnect: bool,
    ) -> ModuleResult<()> {
        self.connect(ObsConnectOptions {
            host: host.into(),
            port,
            password,
            reconnect,
            reconnect_seconds: DEFAULT_RECONNECT_SECONDS,
        })
        .await
    }

    async fn handshake_and_start(
        self: &Arc<Self>,
        options: ObsConnectOptions,
    ) -> ModuleResult<()> {
        {
            let mut s = self.status.write().await;
            s.state = ConnectionState::Connecting;
            s.detail = options.websocket_url();
        }
        self.publish_status().await;

        let url = options.websocket_url();
        let (ws, _) = match tokio::time::timeout(CONNECT_TIMEOUT, connect_async(&url)).await {
            Ok(Ok(pair)) => pair,
            Ok(Err(e)) => {
                self.set_error(e.to_string()).await;
                return Err(ModuleError::Message(e.to_string()));
            }
            Err(_) => {
                let msg = "OBS-Verbindungstimeout".to_string();
                self.set_error(msg.clone()).await;
                return Err(ModuleError::Message(msg));
            }
        };

        let (mut writer, mut reader) = ws.split();

        let hello_text = read_text_frame(&mut reader, "Hello").await.map_err(|e| {
            // can't call async set_error easily in map_err — do below
            e
        });
        let hello_text = match hello_text {
            Ok(t) => t,
            Err(e) => {
                self.set_error(e.to_string()).await;
                return Err(e);
            }
        };

        let hello_env = match decode_envelope(&hello_text) {
            Ok(e) => e,
            Err(e) => {
                self.set_error(e.to_string()).await;
                return Err(e);
            }
        };
        if hello_env.op != HELLO_OP {
            let msg = format!(
                "OBS sendete beim Verbindungsaufbau Op {} statt Hello.",
                hello_env.op
            );
            self.set_error(msg.clone()).await;
            return Err(ModuleError::Message(msg));
        }

        let hello: ObsHello = match serde_json::from_value(hello_env.data) {
            Ok(h) => h,
            Err(e) => {
                let msg = format!("OBS Hello konnte nicht gelesen werden: {e}");
                self.set_error(msg.clone()).await;
                return Err(ModuleError::Message(msg));
            }
        };

        let identify = match create_identify(&hello, options.password.as_deref()) {
            Ok(v) => v,
            Err(e) => {
                self.set_error(e.to_string()).await;
                return Err(e);
            }
        };

        if let Err(e) = writer
            .send(Message::Text(identify.to_string().into()))
            .await
        {
            self.set_error(e.to_string()).await;
            return Err(ModuleError::Message(e.to_string()));
        }

        let identified_text = match read_text_frame(&mut reader, "Identified").await {
            Ok(t) => t,
            Err(e) => {
                self.set_error(e.to_string()).await;
                return Err(e);
            }
        };

        let identified_env = match decode_envelope(&identified_text) {
            Ok(e) => e,
            Err(e) => {
                self.set_error(e.to_string()).await;
                return Err(e);
            }
        };
        if identified_env.op != IDENTIFIED_OP {
            let msg = format!(
                "OBS-Authentifizierung fehlgeschlagen. Empfangener Op: {}.",
                identified_env.op
            );
            self.set_error(msg.clone()).await;
            return Err(ModuleError::Message(msg));
        }
        if let Err(e) = serde_json::from_value::<ObsIdentified>(identified_env.data) {
            let msg = format!("OBS Identified konnte nicht gelesen werden: {e}");
            self.set_error(msg.clone()).await;
            return Err(ModuleError::Message(msg));
        }

        *self.session.lock().await = Some(LiveSession {
            writer,
            pending: HashMap::new(),
        });

        {
            let mut s = self.status.write().await;
            s.state = ConnectionState::Connected;
            s.detail = url;
        }
        self.publish_status().await;

        self.spawn_receive(reader).await;
        Ok(())
    }

    async fn spawn_receive(self: &Arc<Self>, mut reader: WsReader) {
        if let Some(handle) = self.receive_task.lock().await.take() {
            handle.abort();
        }
        let this = Arc::clone(self);
        *self.receive_task.lock().await = Some(tokio::spawn(async move {
            loop {
                match reader.next().await {
                    Some(Ok(Message::Text(text))) => {
                        if let Err(e) = this.handle_incoming(&text).await {
                            debug!(error = %e, "OBS frame handle error");
                        }
                    }
                    Some(Ok(Message::Ping(data))) => {
                        let mut session = this.session.lock().await;
                        if let Some(sess) = session.as_mut() {
                            let _ = sess.writer.send(Message::Pong(data)).await;
                        }
                    }
                    Some(Ok(Message::Close(_))) | None => {
                        warn!("OBS WebSocket closed");
                        break;
                    }
                    Some(Ok(_)) => {}
                    Some(Err(e)) => {
                        warn!(error = %e, "OBS WebSocket read error");
                        break;
                    }
                }
            }
            this.on_connection_lost().await;
        }));
    }

    async fn handle_incoming(&self, text: &str) -> ModuleResult<()> {
        let envelope = decode_envelope(text)?;
        match envelope.op {
            REQUEST_RESPONSE_OP => {
                let response: ObsRequestResponse = serde_json::from_value(envelope.data)
                    .map_err(|e| ModuleError::Message(e.to_string()))?;
                let mut session = self.session.lock().await;
                if let Some(sess) = session.as_mut() {
                    if let Some(tx) = sess.pending.remove(&response.request_id) {
                        let _ = tx.send(response);
                    }
                }
            }
            EVENT_OP => {
                if let Some(scene) = parse_current_program_scene(&envelope.data) {
                    let _ = self.scene_tx.send(scene);
                }
            }
            _ => {}
        }
        Ok(())
    }

    async fn on_connection_lost(self: &Arc<Self>) {
        {
            let mut session = self.session.lock().await;
            if let Some(mut sess) = session.take() {
                fail_pending(&mut sess.pending);
                let _ = sess.writer.close().await;
            }
        }

        if !self.want_connected.load(Ordering::SeqCst) {
            let mut s = self.status.write().await;
            if s.state != ConnectionState::Disconnected {
                s.state = ConnectionState::Disconnected;
                s.detail.clear();
            }
            drop(s);
            self.publish_status().await;
            return;
        }

        {
            let mut s = self.status.write().await;
            if s.state == ConnectionState::Connected {
                s.state = ConnectionState::Error;
                s.detail = "OBS-Verbindung unterbrochen".into();
            }
        }
        self.publish_status().await;

        if self.allow_reconnect.load(Ordering::SeqCst) {
            self.schedule_reconnect();
        }
    }

    fn schedule_reconnect(self: &Arc<Self>) {
        let this = Arc::clone(self);
        // Abort previous reconnect timer if any.
        let fut = async move {
            let seconds = this
                .options
                .read()
                .await
                .as_ref()
                .map(|o| o.reconnect_seconds.max(1))
                .unwrap_or(DEFAULT_RECONNECT_SECONDS);
            tokio::time::sleep(Duration::from_secs(seconds)).await;

            if !this.want_connected.load(Ordering::SeqCst)
                || !this.allow_reconnect.load(Ordering::SeqCst)
            {
                return;
            }
            let options = match this.options.read().await.clone() {
                Some(o) => o,
                None => return,
            };
            {
                let mut s = this.status.write().await;
                s.state = ConnectionState::Connecting;
                s.detail = format!("Reconnect {}", options.websocket_url());
            }
            this.publish_status().await;
            if let Err(e) = this.handshake_and_start(options).await {
                warn!(error = %e, "OBS reconnect failed");
                if this.allow_reconnect.load(Ordering::SeqCst)
                    && this.want_connected.load(Ordering::SeqCst)
                {
                    this.schedule_reconnect();
                }
            }
        };

        // Fire-and-forget; store handle so disconnect can abort.
        let handle = tokio::spawn(fut);
        // Best-effort replace without awaiting (sync context from schedule).
        let this2 = Arc::clone(self);
        tokio::spawn(async move {
            if let Some(old) = this2.reconnect_task.lock().await.replace(handle) {
                old.abort();
            }
        });
    }

    async fn teardown(&self, update_status: bool) {
        self.want_connected.store(false, Ordering::SeqCst);

        if let Some(handle) = self.reconnect_task.lock().await.take() {
            handle.abort();
        }
        if let Some(handle) = self.receive_task.lock().await.take() {
            handle.abort();
        }

        let mut session = self.session.lock().await;
        if let Some(mut sess) = session.take() {
            fail_pending(&mut sess.pending);
            let _ = sess.writer.close().await;
        }

        if update_status {
            let mut s = self.status.write().await;
            s.state = ConnectionState::Disconnected;
            s.detail.clear();
            drop(s);
            self.publish_status().await;
        }
    }

    pub async fn disconnect(&self) -> ModuleResult<()> {
        self.allow_reconnect.store(false, Ordering::SeqCst);
        self.teardown(true).await;
        Ok(())
    }

    pub async fn get_scene_list(&self) -> ModuleResult<Vec<ObsSceneInfo>> {
        let data = self.send_request("GetSceneList", None).await?;
        Ok(parse_scene_list(&data))
    }

    pub async fn set_current_program_scene(&self, scene: &str) -> ModuleResult<()> {
        if scene.trim().is_empty() {
            return Err(ModuleError::Message("Szenenname fehlt.".into()));
        }
        let _ = self
            .send_request(
                "SetCurrentProgramScene",
                Some(json!({ "sceneName": scene })),
            )
            .await?;
        Ok(())
    }

    async fn send_request(
        &self,
        request_type: &str,
        request_data: Option<Value>,
    ) -> ModuleResult<Value> {
        let request_id = uuid::Uuid::new_v4().simple().to_string();
        let (tx, rx) = oneshot::channel();
        let payload = build_request(request_type, &request_id, request_data);

        {
            let mut session = self.session.lock().await;
            let session = session
                .as_mut()
                .ok_or_else(|| ModuleError::Message("OBS ist nicht verbunden.".into()))?;
            session.pending.insert(request_id.clone(), tx);
            if let Err(e) = session
                .writer
                .send(Message::Text(payload.to_string().into()))
                .await
            {
                session.pending.remove(&request_id);
                return Err(ModuleError::Message(e.to_string()));
            }
        }

        let response = match tokio::time::timeout(REQUEST_TIMEOUT, rx).await {
            Ok(Ok(resp)) => resp,
            Ok(Err(_)) => {
                return Err(ModuleError::Message(
                    "OBS-Verbindung wurde getrennt.".into(),
                ));
            }
            Err(_) => {
                let mut session = self.session.lock().await;
                if let Some(s) = session.as_mut() {
                    s.pending.remove(&request_id);
                }
                return Err(ModuleError::Message("OBS Request-Timeout".into()));
            }
        };

        if !response.request_status.result {
            return Err(ModuleError::Message(format!(
                "OBS Request {request_type} fehlgeschlagen ({}): {}",
                response.request_status.code,
                response.request_status.comment.unwrap_or_default()
            )));
        }

        Ok(response.response_data)
    }

    async fn set_error(&self, detail: String) {
        {
            let mut s = self.status.write().await;
            s.state = ConnectionState::Error;
            s.detail = detail;
        }
        self.publish_status().await;
    }
}

fn fail_pending(pending: &mut HashMap<String, oneshot::Sender<ObsRequestResponse>>) {
    for (_, tx) in pending.drain() {
        let _ = tx.send(ObsRequestResponse {
            request_type: String::new(),
            request_id: String::new(),
            request_status: ObsRequestStatus {
                result: false,
                code: 0,
                comment: Some("OBS-Verbindung wurde getrennt.".into()),
            },
            response_data: Value::Object(Default::default()),
        });
    }
}

async fn read_text_frame(reader: &mut WsReader, label: &str) -> ModuleResult<String> {
    match tokio::time::timeout(CONNECT_TIMEOUT, reader.next()).await {
        Ok(Some(Ok(Message::Text(text)))) => Ok(text.to_string()),
        Ok(Some(Ok(_))) => Err(ModuleError::Message(format!(
            "OBS sendete kein Text-Frame bei {label}."
        ))),
        Ok(Some(Err(e))) => Err(ModuleError::Message(e.to_string())),
        Ok(None) => Err(ModuleError::Message(format!(
            "OBS hat die Verbindung vor {label} geschlossen."
        ))),
        Err(_) => Err(ModuleError::Message(format!("OBS {label}-Timeout"))),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use futures_util::{SinkExt, StreamExt};
    use protocol::create_authentication_response;
    use tokio::net::TcpListener;
    use tokio_tungstenite::accept_async;

    async fn start_mock_server(
        with_auth: bool,
        password: &str,
        close_after_identify: bool,
    ) -> (u16, JoinHandle<()>) {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let password = password.to_string();

        let handle = tokio::spawn(async move {
            let (stream, _) = listener.accept().await.unwrap();
            let mut ws = accept_async(stream).await.unwrap();

            let hello = if with_auth {
                json!({
                    "op": 0,
                    "d": {
                        "obsWebSocketVersion": "5.6.0",
                        "rpcVersion": 1,
                        "authentication": {
                            "challenge": "contract-challenge",
                            "salt": "contract-salt"
                        }
                    }
                })
            } else {
                json!({
                    "op": 0,
                    "d": {
                        "obsWebSocketVersion": "5.6.0",
                        "rpcVersion": 1
                    }
                })
            };
            ws.send(Message::Text(hello.to_string().into()))
                .await
                .unwrap();

            let identify_msg = match ws.next().await {
                Some(Ok(Message::Text(t))) => t.to_string(),
                other => panic!("expected Identify text, got {other:?}"),
            };
            let identify: Value = serde_json::from_str(&identify_msg).unwrap();
            assert_eq!(identify["op"], 1);
            assert_eq!(identify["d"]["rpcVersion"], 1);
            assert_eq!(identify["d"]["eventSubscriptions"], DEFAULT_EVENT_SUBSCRIPTIONS);
            if with_auth {
                let expected =
                    create_authentication_response(&password, "contract-salt", "contract-challenge");
                assert_eq!(identify["d"]["authentication"], expected);
            } else {
                assert!(identify["d"].get("authentication").is_none());
            }

            ws.send(Message::Text(
                json!({"op": 2, "d": {"negotiatedRpcVersion": 1}})
                    .to_string()
                    .into(),
            ))
            .await
            .unwrap();

            if close_after_identify {
                let _ = ws.close(None).await;
                return;
            }

            while let Some(Ok(msg)) = ws.next().await {
                let Message::Text(text) = msg else {
                    continue;
                };
                let req: Value = serde_json::from_str(&text).unwrap();
                if req["op"] != 6 {
                    continue;
                }
                let request_type = req["d"]["requestType"].as_str().unwrap_or("");
                let request_id = req["d"]["requestId"].as_str().unwrap_or("").to_string();
                let response = match request_type {
                    "GetSceneList" => json!({
                        "op": 7,
                        "d": {
                            "requestType": "GetSceneList",
                            "requestId": request_id,
                            "requestStatus": { "result": true, "code": 100 },
                            "responseData": {
                                "currentProgramSceneName": "Live",
                                "scenes": [
                                    { "sceneName": "Start", "sceneIndex": 0 },
                                    { "sceneName": "Live", "sceneIndex": 1 }
                                ]
                            }
                        }
                    }),
                    "SetCurrentProgramScene" => {
                        let scene = req["d"]["requestData"]["sceneName"]
                            .as_str()
                            .unwrap_or("")
                            .to_string();
                        assert!(!scene.is_empty());
                        json!({
                            "op": 7,
                            "d": {
                                "requestType": "SetCurrentProgramScene",
                                "requestId": request_id,
                                "requestStatus": { "result": true, "code": 100 },
                                "responseData": {}
                            }
                        })
                    }
                    _ => json!({
                        "op": 7,
                        "d": {
                            "requestType": request_type,
                            "requestId": request_id,
                            "requestStatus": {
                                "result": false,
                                "code": 500,
                                "comment": "unknown"
                            },
                            "responseData": {}
                        }
                    }),
                };
                ws.send(Message::Text(response.to_string().into()))
                    .await
                    .unwrap();
            }
        });

        (port, handle)
    }

    #[tokio::test]
    async fn connect_handshake_without_auth() {
        let (port, server) = start_mock_server(false, "", false).await;
        let client = ObsClient::new_shared("127.0.0.1", port);
        client
            .connect_simple("127.0.0.1", port, None, false)
            .await
            .unwrap();
        let status = client.status().await;
        assert_eq!(status.state, ConnectionState::Connected);
        client.disconnect().await.unwrap();
        let _ = server.await;
    }

    #[tokio::test]
    async fn connect_handshake_with_auth() {
        let (port, server) = start_mock_server(true, "contract-password", false).await;
        let client = ObsClient::new_shared("127.0.0.1", port);
        client
            .connect_simple(
                "127.0.0.1",
                port,
                Some("contract-password".into()),
                false,
            )
            .await
            .unwrap();
        assert_eq!(client.status().await.state, ConnectionState::Connected);
        client.disconnect().await.unwrap();
        let _ = server.await;
    }

    #[tokio::test]
    async fn get_scene_list_and_set_scene_roundtrip() {
        let (port, server) = start_mock_server(false, "", false).await;
        let client = ObsClient::new_shared("127.0.0.1", port);
        client
            .connect_simple("127.0.0.1", port, None, false)
            .await
            .unwrap();

        let scenes = client.get_scene_list().await.unwrap();
        assert_eq!(scenes.len(), 2);
        assert_eq!(scenes[0].name, "Start");
        assert_eq!(scenes[1].name, "Live");

        client.set_current_program_scene("Live").await.unwrap();
        client.disconnect().await.unwrap();
        let _ = server.await;
    }

    #[tokio::test]
    async fn set_scene_payload_shape() {
        let client = ObsClient::new("127.0.0.1", 4455);
        let msg = client.set_scene("Live").await;
        assert_eq!(msg["d"]["requestType"], "SetCurrentProgramScene");
        assert_eq!(msg["d"]["requestData"]["sceneName"], "Live");
    }

    #[tokio::test]
    async fn identify_is_obs_v5() {
        let msg = ObsClient::identify_message(1);
        assert_eq!(msg["op"], 1);
        assert_eq!(msg["d"]["rpcVersion"], 1);
    }

    #[tokio::test]
    async fn disconnect_stops_reconnect_after_server_close() {
        let (port, server) = start_mock_server(false, "", true).await;
        let client = ObsClient::new_shared("127.0.0.1", port);
        client
            .connect(ObsConnectOptions {
                host: "127.0.0.1".into(),
                port,
                password: None,
                reconnect: true,
                reconnect_seconds: 1,
            })
            .await
            .unwrap();

        // Wait for server close to be observed.
        tokio::time::sleep(Duration::from_millis(200)).await;
        client.disconnect().await.unwrap();
        assert_eq!(client.status().await.state, ConnectionState::Disconnected);

        // Ensure reconnect does not flip back to connecting.
        tokio::time::sleep(Duration::from_millis(1200)).await;
        assert_eq!(client.status().await.state, ConnectionState::Disconnected);
        let _ = server.await;
    }
}
