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
    tx: broadcast::Sender<String>,
    clients: Arc<AtomicUsize>,
    sockets: Arc<RwLock<HashMap<Uuid, ()>>>,
}

impl RealtimeHub {
    pub fn new() -> Self {
        let (tx, _) = broadcast::channel(256);
        Self {
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
        let _ = self.tx.send(event.to_string());
    }

    pub fn publish_raw(&self, payload: impl Into<String>) {
        let _ = self.tx.send(payload.into());
    }

    pub async fn handle_socket(&self, socket: WebSocket) {
        let id = Uuid::new_v4();
        self.clients.fetch_add(1, Ordering::Relaxed);
        self.sockets.write().await.insert(id, ());

        let (mut sink, mut stream) = socket.split();
        let mut rx = self.tx.subscribe();

        let hello = serde_json::json!({
            "type": "app.ws.hello",
            "clientId": id.to_string(),
        });
        let _ = sink.send(Message::Text(hello.to_string().into())).await;

        loop {
            tokio::select! {
                incoming = stream.next() => {
                    match incoming {
                        Some(Ok(Message::Text(text))) => {
                            if let Ok(value) = serde_json::from_str::<Value>(&text) {
                                let ty = value.get("type").and_then(|v| v.as_str()).unwrap_or("");
                                if ty.starts_with("editor.layout.") {
                                    self.publish(&value);
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
