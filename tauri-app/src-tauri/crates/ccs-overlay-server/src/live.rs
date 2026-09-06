use chrono::{DateTime, Utc};
use serde_json::{json, Value};
use std::{
    path::PathBuf,
    sync::{Mutex, RwLock},
};

pub struct LiveState {
    pub data: RwLock<Value>,
    history: Mutex<History>,
    countdown: Mutex<(Option<DateTime<Utc>>, i64, String)>,
}
struct History {
    events: Vec<Value>,
    path: Option<PathBuf>,
    dirty: bool,
}
impl Default for LiveState {
    fn default() -> Self {
        Self {
            data: RwLock::new(
                json!({"stream":{},"twitch":{},"spotify":{},"music":{},"obs":{},"alerts":{},"stats":{},"branding":{}}),
            ),
            history: Mutex::new(History {
                events: vec![],
                path: None,
                dirty: false,
            }),
            countdown: Mutex::new((None, 0, String::new())),
        }
    }
}
impl LiveState {
    pub fn configure_history(&self, path: PathBuf) -> Result<(), String> {
        let mut history = self.history.lock().unwrap();
        if history.path.is_some() {
            return Ok(());
        }
        if path.exists() {
            let value: Value =
                serde_json::from_slice(&std::fs::read(&path).map_err(|e| e.to_string())?)
                    .map_err(|e| e.to_string())?;
            history.events = value["events"].as_array().cloned().unwrap_or_default();
            let excess = history.events.len().saturating_sub(1000);
            history.events.drain(..excess);
        }
        history.path = Some(path);
        Ok(())
    }
    pub fn history(&self) -> Value {
        json!({"events":self.history.lock().unwrap().events})
    }
    pub fn record(&self, event: &Value) {
        let kind = event["type"].as_str().unwrap_or("");
        let mut history = self.history.lock().unwrap();
        match kind {
            "channel.chat.message" => {
                let id = &event["data"]["messageId"];
                if !id.is_null() && history.events.iter().any(|v| &v["data"]["messageId"] == id) {
                    return;
                }
                history.events.push(event.clone());
                let excess = history.events.len().saturating_sub(1000);
                history.events.drain(..excess);
            }
            "app.chat.clear" | "channel.chat.clear" => history.events.clear(),
            "channel.chat.message_delete" => {
                let id = event["data"]
                    .get("messageId")
                    .or_else(|| event["data"].get("message_id"));
                if let Some(id) = id {
                    history.events.retain(|v| &v["data"]["messageId"] != id);
                }
            }
            "channel.chat.clear_user_messages" => {
                let id = event["data"]
                    .get("targetUserId")
                    .or_else(|| event["data"].get("target_user_id"));
                if let Some(id) = id {
                    history.events.retain(|v| &v["data"]["userId"] != id);
                }
            }
            _ => return,
        }
        history.dirty = true;
    }
    pub fn flush_history(&self) -> Result<(), String> {
        let mut history = self.history.lock().unwrap();
        if !history.dirty {
            return Ok(());
        }
        if let Some(path) = &history.path {
            if let Some(parent) = path.parent() {
                std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
            }
            let temp = path.with_extension("json.tmp");
            std::fs::write(&temp, json!({"events":history.events}).to_string())
                .map_err(|e| e.to_string())?;
            std::fs::rename(temp, path).map_err(|e| e.to_string())?;
            history.dirty = false;
        }
        Ok(())
    }
    pub fn set_countdown(&self, seconds: i64, label: &str) -> Result<(), String> {
        if !(0..=86400).contains(&seconds) {
            return Err("Countdown muss zwischen 0 und 86400 Sekunden liegen".into());
        }
        *self.countdown.lock().unwrap() = (
            if seconds > 0 {
                Some(Utc::now() + chrono::Duration::seconds(seconds))
            } else {
                None
            },
            seconds,
            label.to_string(),
        );
        Ok(())
    }
    pub fn countdown_state(&self) -> Value {
        let event = self.countdown();
        let data = &event["data"];
        json!({"isRunning":data["isRunning"]=="true",
            "remainingSeconds":data["remainingSeconds"].as_str().and_then(|v|v.parse::<i64>().ok()).unwrap_or(0),
            "totalSeconds":data["totalSeconds"].as_str().and_then(|v|v.parse::<i64>().ok()).unwrap_or(0),
            "endsAt":data["endsAt"],"label":data["label"],"mode":"manual"})
    }
    pub fn countdown(&self) -> Value {
        let countdown = self.countdown.lock().unwrap();
        let remaining = countdown
            .0
            .map(|end| ((end - Utc::now()).num_milliseconds() + 999) / 1000)
            .unwrap_or(0)
            .max(0);
        json!({"source":"app","type":"app.countdown","at":Utc::now().to_rfc3339(),"summary":countdown.2,"data":{"isRunning":(remaining>0).to_string(),"remainingSeconds":remaining.to_string(),"totalSeconds":countdown.1.to_string(),"label":countdown.2,"endsAt":countdown.0.map(|t|t.to_rfc3339()).unwrap_or_default()}})
    }
}
