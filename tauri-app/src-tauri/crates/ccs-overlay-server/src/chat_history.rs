use serde_json::{json, Value};
use std::{path::PathBuf, sync::Mutex};

/// Shared chat retention and moderation, with independent capacities for app and overlay.
pub struct ChatHistoryBuffer {
    history: Mutex<History>,
}
struct History {
    events: Vec<Value>,
    path: Option<PathBuf>,
    dirty: bool,
    capacity: usize,
}
fn is_chat_message(event: &Value) -> bool {
    event["type"] == "channel.chat.message"
        && event["source"]
            .as_str()
            .is_some_and(|s| s.eq_ignore_ascii_case("twitch"))
}
impl ChatHistoryBuffer {
    pub fn with_capacity(capacity: usize) -> Self {
        Self {
            history: Mutex::new(History {
                events: vec![],
                path: None,
                dirty: false,
                capacity: capacity.min(2000),
            }),
        }
    }
    pub fn configure_history(&self, path: PathBuf) -> Result<(), String> {
        let mut history = self.history.lock().unwrap();
        if history.path.as_ref() == Some(&path) {
            return Ok(());
        }
        let restored = if path.exists() {
            let value: Value =
                serde_json::from_slice(&std::fs::read(&path).map_err(|e| e.to_string())?)
                    .map_err(|e| e.to_string())?;
            if value.is_null() {
                Vec::new()
            } else {
                value
                    .as_array()
                    .or_else(|| value.get("events").and_then(Value::as_array))
                    .ok_or_else(|| {
                        "Chat-Verlauf muss eine Liste oder einen events-Envelope enthalten"
                            .to_string()
                    })?
                    .iter()
                    .filter(|e| is_chat_message(e))
                    .cloned()
                    .collect::<Vec<_>>()
            }
        } else {
            Vec::new()
        };
        // Validate the target first; a bad path/file must not replace the active buffer.
        Self::flush_buffer(&mut history)?;
        if history.path.is_some() || !restored.is_empty() {
            history.events = restored;
        }
        let excess = history.events.len().saturating_sub(history.capacity);
        history.events.drain(..excess);
        history.path = Some(path);
        Ok(())
    }
    pub fn chat_capacity(&self) -> usize {
        self.history.lock().unwrap().capacity
    }
    pub fn configure_chat_buffer(&self, capacity: usize) -> bool {
        let mut history = self.history.lock().unwrap();
        let capacity = capacity.min(2000);
        let changed = history.capacity != capacity;
        history.capacity = capacity;
        let excess = history.events.len().saturating_sub(capacity);
        if excess > 0 {
            history.events.drain(..excess);
            history.dirty = true;
        }
        changed
    }
    pub fn history(&self) -> Value {
        json!({"events":self.history.lock().unwrap().events})
    }
    pub fn record(&self, event: &Value) -> bool {
        let kind = event["type"].as_str().unwrap_or("");
        if kind == "channel.chat.message" && !is_chat_message(event) {
            return false;
        }
        let mut history = self.history.lock().unwrap();
        match kind {
            "channel.chat.message" => {
                let id = &event["data"]["messageId"];
                if !id.is_null() && history.events.iter().any(|v| &v["data"]["messageId"] == id) {
                    return false;
                }
                history.events.push(event.clone());
                let excess = history.events.len().saturating_sub(history.capacity);
                history.events.drain(..excess);
            }
            "app.chat.clear" | "channel.chat.clear" => history.events.clear(),
            "channel.chat.message_delete" => {
                let id = event["data"]
                    .get("messageId")
                    .or_else(|| event["data"].get("message_id"));
                if let Some(id) = id
                    .and_then(Value::as_str)
                    .map(str::trim)
                    .filter(|s| !s.is_empty())
                {
                    history.events.retain(|v| {
                        !v["data"]["messageId"]
                            .as_str()
                            .is_some_and(|s| s.eq_ignore_ascii_case(id))
                    });
                }
            }
            "channel.chat.clear_user_messages" => {
                let id = event["data"]
                    .get("targetUserId")
                    .or_else(|| event["data"].get("target_user_id"));
                let id = id.and_then(Value::as_str).unwrap_or("").trim();
                let login = event["data"]
                    .get("targetUserLogin")
                    .or_else(|| event["data"].get("target_user_login"))
                    .and_then(Value::as_str)
                    .unwrap_or("")
                    .trim();
                history.events.retain(|v| {
                    !(!id.is_empty() && v["data"]["userId"].as_str() == Some(id)
                        || !login.is_empty()
                            && v["data"]["userLogin"]
                                .as_str()
                                .is_some_and(|s| s.eq_ignore_ascii_case(login)))
                });
            }
            _ => return false,
        }
        history.dirty = true;
        true
    }
    pub fn flush_history(&self) -> Result<(), String> {
        let mut history = self.history.lock().unwrap();
        Self::flush_buffer(&mut history)
    }
    fn flush_buffer(history: &mut History) -> Result<(), String> {
        if !history.dirty {
            return Ok(());
        }
        if let Some(path) = &history.path {
            if let Some(parent) = path.parent() {
                std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
            }
            let temp = path.with_extension("json.tmp");
            std::fs::write(
                &temp,
                serde_json::to_vec(&history.events).map_err(|e| e.to_string())?,
            )
            .map_err(|e| e.to_string())?;
            std::fs::rename(temp, path).map_err(|e| e.to_string())?;
            history.dirty = false;
        }
        Ok(())
    }
}
