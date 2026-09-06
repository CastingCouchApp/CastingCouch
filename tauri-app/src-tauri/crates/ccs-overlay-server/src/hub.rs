use axum::extract::ws::{Message, WebSocket};
use futures_util::{SinkExt, StreamExt};
use serde_json::Value;
use std::collections::HashMap;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::Arc;
use tokio::sync::{broadcast, RwLock};
use uuid::Uuid;

#[derive(Clone)]
pub struct RealtimeHub {
    pub live: Arc<crate::live::LiveState>,
    pub obs: Arc<std::sync::RwLock<Option<Arc<dyn crate::ObsOverlayProvider>>>>,
    tx: broadcast::Sender<String>,
    clients: Arc<AtomicUsize>,
    sockets: Arc<RwLock<HashMap<Uuid, ()>>>,
}

impl RealtimeHub {
    pub fn new() -> Self {
        let (tx, _) = broadcast::channel(256);
        Self {
            live: Arc::new(crate::live::LiveState::default()),
            obs: Arc::new(std::sync::RwLock::new(None)),
            tx,
            clients: Arc::new(AtomicUsize::new(0)),
            sockets: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    pub fn connected_clients(&self) -> usize {
        self.clients.load(Ordering::Relaxed)
    }

    pub fn subscribe(&self) -> broadcast::Receiver<String> {
        self.tx.subscribe()
    }

    pub fn publish(&self, event: &Value) {
        self.live.record(event);
        let _ = self.tx.send(event.to_string());
    }

    pub fn configure_history(&self, path: std::path::PathBuf) -> Result<(), String> {
        self.live.configure_history(path)
    }
    pub fn history(&self) -> Value {
        self.live.history()
    }
    pub fn flush_history(&self) -> Result<(), String> {
        self.live.flush_history()
    }
    pub fn set_countdown(&self, seconds: i64, label: &str) -> Result<(), String> {
        self.live.set_countdown(seconds, label)?;
        self.publish(&self.countdown());
        Ok(())
    }
    pub fn countdown(&self) -> Value {
        self.live.countdown()
    }

    pub fn publish_raw(&self, payload: impl Into<String>) {
        let _ = self.tx.send(payload.into());
    }

    pub async fn handle_socket(
        &self,
        socket: WebSocket,
        layouts: crate::OverlayLayoutStore,
        canvases: Vec<ccs_core::OverlayCanvasSettings>,
        mut shutdown: Option<tokio::sync::watch::Receiver<bool>>,
    ) {
        let id = Uuid::new_v4();
        self.clients.fetch_add(1, Ordering::Relaxed);
        self.sockets.write().await.insert(id, ());

        let (mut sink, mut stream) = socket.split();
        let mut rx = self.tx.subscribe();

        let mut hello_data = serde_json::json!({"clientId":id.to_string(),"clients":self.connected_clients().to_string()});
        for (index, canvas) in canvases.iter().enumerate() {
            hello_data[format!("overlay.{index}.id")] = serde_json::json!(canvas.id);
            hello_data[format!("overlay.{index}.name")] = serde_json::json!(canvas.name);
        }
        let hello = serde_json::json!({
            "source":"app",
            "type": "app.ws.hello",
            "at":chrono::Utc::now().to_rfc3339(),
            "summary":"Verbunden",
            "data":hello_data,
        });
        let _ = sink.send(Message::Text(hello.to_string().into())).await;
        let _ = sink
            .send(Message::Text(self.countdown().to_string().into()))
            .await;
        let history = self.history()["events"]
            .as_array()
            .cloned()
            .unwrap_or_default();
        for event in history {
            let _ = sink.send(Message::Text(event.to_string().into())).await;
        }

        loop {
            tokio::select! {
                _ = async {match shutdown.as_mut(){Some(rx)=>{let _=rx.wait_for(|v|*v).await;},None=>std::future::pending::<()>().await}} => break,
                incoming = stream.next() => {
                    match incoming {
                        Some(Ok(Message::Text(text))) => {
                            if let Ok(value) = serde_json::from_str::<Value>(&text) {
                                let ty = value.get("type").and_then(|v| v.as_str()).unwrap_or("");
                                if matches!(ty, "editor.layout.set" | "editor.layout.patch") {
                                    let data = &value["data"];
                                    if let Some(id) = data["instanceId"].as_str() {
                                        let layout = match &data["layout"] {
                                            Value::String(text) => serde_json::from_str::<Value>(text).ok(),
                                            Value::Object(_) => Some(data["layout"].clone()),
                                            _ => None,
                                        };
                                        if let Some(layout) = layout.filter(Value::is_object) {
                                            if layouts.save(id, &layout).await.is_ok() {
                                                self.publish(&serde_json::json!({"source":"app","type":"app.overlay.layout","at":chrono::Utc::now().to_rfc3339(),"summary":"Layout gespeichert","data":{"instanceId":id,"layout":layout.to_string()}}));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        Some(Ok(Message::Ping(p))) => {
                            let _ = sink.send(Message::Pong(p)).await;
                        }
                        Some(Ok(Message::Close(_))) | None => break,
                        Some(Ok(_)) => {}
                        Some(Err(_)) => break,
                    }
                }
                outbound = rx.recv() => {
                    match outbound {
                        Ok(payload) => {
                            if sink.send(Message::Text(payload.into())).await.is_err() {
                                break;
                            }
                        }
                        Err(broadcast::error::RecvError::Lagged(_)) => continue,
                        Err(broadcast::error::RecvError::Closed) => break,
                    }
                }
            }
        }

        let _ = sink.send(Message::Close(None)).await;
        self.sockets.write().await.remove(&id);
        self.clients.fetch_sub(1, Ordering::Relaxed);
    }
}

impl Default for RealtimeHub {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[tokio::test]
    async fn publish_delivers_json_to_subscribers() {
        let hub = RealtimeHub::new();
        let mut rx = hub.subscribe();
        hub.publish(&json!({
            "source": "twitch",
            "type": "channel.follow",
            "summary": "Neuer Follower",
            "data": { "user": "alice" }
        }));

        let payload = rx.recv().await.expect("published frame");
        let root: Value = serde_json::from_str(&payload).unwrap();
        assert_eq!(root["source"], "twitch");
        assert_eq!(root["type"], "channel.follow");
        assert_eq!(root["summary"], "Neuer Follower");
        assert_eq!(root["data"]["user"], "alice");
    }
}
