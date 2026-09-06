use chrono::{DateTime, Utc};
use serde_json::{json, Value};
use std::{
    path::PathBuf,
    sync::{Mutex, RwLock},
};

pub struct LiveState {
    pub data: RwLock<Value>,
    history: Mutex<History>,
    viewer_samples: Mutex<(u64, u64)>,
    countdown: Mutex<(Option<DateTime<Utc>>, i64, String)>,
}
struct History {
    events: Vec<Value>,
    path: Option<PathBuf>,
    dirty: bool,
}
impl Default for LiveState {
    fn default() -> Self {
        let mut initial: Value = serde_json::from_str(include_str!("snapshot-defaults.json"))
            .expect("valid snapshot defaults");
        initial["updatedAt"] = json!(Utc::now().to_rfc3339());
        Self {
            data: RwLock::new(initial),
            viewer_samples: Mutex::new((0, 0)),
            history: Mutex::new(History {
                events: vec![],
                path: None,
                dirty: false,
            }),
            countdown: Mutex::new((None, 0, String::new())),
        }
    }
}
fn merge(target: &mut Value, patch: &Value) {
    if let (Some(target), Some(patch)) = (target.as_object_mut(), patch.as_object()) {
        for (key, value) in patch {
            merge(target.entry(key).or_insert(Value::Null), value);
        }
    } else {
        *target = patch.clone();
    }
}
impl LiveState {
    pub fn update_twitch(
        &self,
        settings: &Value,
        channel: &Value,
        stream: &Value,
        followers: &Value,
        subs: &Value,
    ) {
        let mut data = self.data.write().unwrap();
        data["twitch"]["channelName"] = settings
            .get("ChannelName")
            .cloned()
            .unwrap_or_else(|| json!(""));
        data["twitch"]["available"] = json!(!channel.is_null());
        data["twitch"]["title"] = channel
            .pointer("/data/0/title")
            .cloned()
            .unwrap_or_else(|| json!(""));
        data["twitch"]["category"] = channel
            .pointer("/data/0/game_name")
            .cloned()
            .unwrap_or_else(|| json!(""));
        data["twitch"]["followers"] = followers.get("total").cloned().unwrap_or_else(|| json!(0));
        data["twitch"]["subscriptions"] = subs.get("total").cloned().unwrap_or_else(|| json!(0));
        data["stream"]["viewerCount"] = stream
            .pointer("/data/0/viewer_count")
            .cloned()
            .unwrap_or_else(|| json!(0));
        data["twitch"]["viewerCountAvailable"] = json!(!stream.is_null());
        for (key, target, title) in [
            ("FollowerGoal", 200, "Follower-Ziel"),
            ("SubGoal", 25, "Sub-Ziel"),
            ("DonationGoal", 100, "Donation-Ziel"),
        ] {
            let goal = &settings[key];
            let output = format!("{}{}State", key[..1].to_lowercase(), &key[1..]);
            data["twitch"][&output] = json!({"title":goal.get("Title").cloned().unwrap_or_else(||json!(title)),"reason":goal.get("Reason").cloned().unwrap_or_else(||json!("")),"current":goal.get("Current").cloned().unwrap_or_else(||json!(0)),"target":goal.get("Target").cloned().unwrap_or_else(||json!(target)),"fontFace":goal.get("FontFace").cloned().unwrap_or_else(||json!("Segoe UI")),"fontSize":goal.get("FontSize").cloned().unwrap_or_else(||json!(36)),"currency":goal.get("Currency").cloned().unwrap_or_else(||json!("")),"enabled":goal.get("Enabled").cloned().unwrap_or_else(||json!(true))});
        }
        if let Some(total) = followers.get("total") {
            data["twitch"]["followerGoalState"]["current"] = total.clone();
        }
        if let Some(total) = subs.get("total") {
            data["twitch"]["subGoalState"]["current"] = total.clone();
        }
        data["twitch"]["followerGoal"] = data["twitch"]["followerGoalState"]["target"].clone();
        if data["stream"]["isLive"] == true && !stream.is_null() {
            let viewers = data["stream"]["viewerCount"].as_u64().unwrap_or(0);
            let mut samples = self.viewer_samples.lock().unwrap();
            samples.0 += 1;
            samples.1 += viewers;
            data["stats"]["averageViewers"] = json!(samples.1 as f64 / samples.0 as f64);
            data["stats"]["peakViewers"] =
                json!(viewers.max(data["stats"]["peakViewers"].as_u64().unwrap_or(0)));
        }
    }

    pub fn merge_snapshot(&self, patch: &Value) -> Value {
        let mut data = self.data.write().unwrap();
        let was_live = data["stream"]["isLive"] == true;
        merge(&mut data, patch);
        let live = data["stream"]["isLive"] == true;
        let now = Utc::now();
        if live && !was_live {
            let defaults: Value =
                serde_json::from_str(include_str!("snapshot-defaults.json")).unwrap();
            data["stats"] = defaults["stats"].clone();
            *self.viewer_samples.lock().unwrap() = (0, 0);
            data["stream"]["startedAt"] = json!((now
                - chrono::Duration::seconds(
                    data["stream"]["elapsedSeconds"].as_i64().unwrap_or(0)
                ))
            .to_rfc3339());
            data["stream"]["endedAt"] = Value::Null;
        } else if !live && was_live {
            data["stream"]["endedAt"] = json!(now.to_rfc3339());
        }
        data["stream"]["phase"] = json!(if live { "Live" } else { "Idle" });
        data["stats"]["streamTimeSeconds"] = data["stream"]["elapsedSeconds"].clone();
        data["updatedAt"] = json!(now.to_rfc3339());
        data.clone()
    }
    /// Preserve the existing file node because imported overlays may hardlink it.
    pub async fn write_snapshot(&self, path: &std::path::Path) -> Result<(), std::io::Error> {
        let mut output = tokio::fs::read(path)
            .await
            .ok()
            .and_then(|b| serde_json::from_slice::<Value>(&b).ok())
            .filter(Value::is_object)
            .unwrap_or_else(|| json!({}));
        let managed: Value = serde_json::from_str(include_str!("snapshot-defaults.json")).unwrap();
        let owned =
            |key: &str| managed.get(key).is_some() || matches!(key, "serverError" | "dataError");
        let snapshot = self.data.read().unwrap().clone();
        for (key, value) in snapshot.as_object().unwrap() {
            if owned(key) {
                output[key] = value.clone();
            }
        }
        {
            let mut data = self.data.write().unwrap();
            data.as_object_mut()
                .unwrap()
                .retain(|key, _| owned(key) || output.get(key).is_some());
            for (key, value) in output.as_object().unwrap() {
                if !owned(key) {
                    data[key] = value.clone();
                }
            }
        }
        if let Some(parent) = path.parent() {
            tokio::fs::create_dir_all(parent).await?;
        }
        tokio::fs::write(path, output.to_string()).await
    }

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
        if kind == "channel.chat.message" {
            let history = self.history.lock().unwrap();
            let id = &event["data"]["messageId"];
            if !id.is_null() && history.events.iter().any(|v| &v["data"]["messageId"] == id) {
                return;
            }
        }
        {
            let mut data = self.data.write().unwrap();
            if kind.starts_with("channel.") {
                data["twitch"]["lastEvent"] =
                    event.get("summary").cloned().unwrap_or_else(|| json!(kind));
            }
            if kind == "channel.follow" {
                data["twitch"]["lastFollower"] = event["data"]
                    .get("user")
                    .or_else(|| event["data"].get("user_name"))
                    .cloned()
                    .unwrap_or_else(|| json!(""));
            }
            if data["stream"]["isLive"] == true {
                let metric = match kind {
                    "channel.follow" => Some(("followersGained", 1)),
                    "channel.chat.message" => Some(("chatMessages", 1)),
                    "channel.subscribe" => Some(("newSubscriptions", 1)),
                    "channel.subscription.gift" => Some((
                        "giftSubscriptions",
                        event["data"]["total"]
                            .as_i64()
                            .or_else(|| {
                                event["data"]["total"].as_str().and_then(|s| s.parse().ok())
                            })
                            .unwrap_or(1),
                    )),
                    "channel.cheer" => Some((
                        "bitsCheered",
                        event["data"]["bits"]
                            .as_i64()
                            .or_else(|| event["data"]["bits"].as_str().and_then(|s| s.parse().ok()))
                            .unwrap_or(0),
                    )),
                    "channel.raid" => Some(("incomingRaids", 1)),
                    "app.alert" => Some(("alertsPlayed", 1)),
                    _ => None,
                };
                if let Some((key, amount)) = metric {
                    data["stats"][key] = json!(data["stats"][key].as_i64().unwrap_or(0) + amount);
                }
            }
        }
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
