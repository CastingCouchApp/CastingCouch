use serde::{Deserialize, Serialize};
use serde_json::Value;
use tokio::sync::RwLock;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AlertDefinition {
    pub id: String,
    pub name: String,
    pub event_type: String,
    pub enabled: bool,
}

pub struct AlertEngine {
    definitions: RwLock<Vec<AlertDefinition>>,
    queue: RwLock<Vec<Value>>,
}

impl AlertEngine {
    pub fn new() -> Self {
        Self {
            definitions: RwLock::new(vec![]),
            queue: RwLock::new(vec![]),
        }
    }

    pub async fn list(&self) -> Vec<AlertDefinition> {
        self.definitions.read().await.clone()
    }

    pub async fn upsert(&self, def: AlertDefinition) {
        let mut defs = self.definitions.write().await;
        if let Some(existing) = defs.iter_mut().find(|d| d.id == def.id) {
            *existing = def;
        } else {
            defs.push(def);
        }
    }

    pub async fn enqueue(&self, event: Value) {
        self.queue.write().await.push(event);
    }

    pub async fn pending_count(&self) -> usize {
        self.queue.read().await.len()
    }
}

impl Default for AlertEngine {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn upsert_and_queue() {
        let engine = AlertEngine::new();
        engine
            .upsert(AlertDefinition {
                id: "follow".into(),
                name: "Follow".into(),
                event_type: "channel.follow".into(),
                enabled: true,
            })
            .await;
        engine.enqueue(serde_json::json!({ "type": "channel.follow" })).await;
        assert_eq!(engine.list().await.len(), 1);
        assert_eq!(engine.pending_count().await, 1);
    }
}
