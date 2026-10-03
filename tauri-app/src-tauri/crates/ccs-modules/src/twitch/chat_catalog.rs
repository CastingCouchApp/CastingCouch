#[cfg(test)]
#[path = "chat_catalog_tests.rs"]
mod tests;
use super::{TwitchEvent, TwitchHelixClient};
use ccs_core::settings::OverlayChatSettings;
use serde::Serialize;
use serde_json::{json, Value};
use std::{collections::BTreeMap, sync::RwLock, time::Duration};

type Map = BTreeMap<String, Value>;

#[derive(Default)]
struct BadgeDefinitions {
    versions: Map,
    by_set: Map,
}

#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ChatCatalogStatus {
    pub channel_id: String,
    pub emotes: usize,
    pub badges: usize,
    pub errors: Vec<String>,
    pub updated_at: Option<String>,
}

#[derive(Default)]
struct Snapshot {
    bttv: Map,
    ffz: Map,
    seven_tv: Map,
    global_badges: BadgeDefinitions,
    channel_badges: BadgeDefinitions,
    status: ChatCatalogStatus,
}

pub(super) struct ChatCatalogs {
    http: reqwest::Client,
    bases: [String; 3],
    data: RwLock<Snapshot>,
}

impl ChatCatalogs {
    pub fn new() -> Self {
        Self::with_bases(
            "https://api.betterttv.net/3/cached".into(),
            "https://api.frankerfacez.com/v1".into(),
            "https://7tv.io/v3".into(),
        )
    }
    fn with_bases(bttv: String, ffz: String, seven_tv: String) -> Self {
        Self {
            http: reqwest::Client::builder()
                .timeout(Duration::from_secs(5))
                .build()
                .expect("HTTP client"),
            bases: [bttv, ffz, seven_tv],
            data: RwLock::new(Snapshot::default()),
        }
    }
    pub fn status(&self) -> ChatCatalogStatus {
        self.data.read().unwrap().status.clone()
    }
    pub fn clear(&self) {
        *self.data.write().unwrap() = Snapshot::default();
    }
    pub fn error(&self, error: String) -> ChatCatalogStatus {
        let mut data = self.data.write().unwrap();
        data.status.errors = vec![error];
        data.status.clone()
    }
    async fn fetch(
        &self,
        request: reqwest::RequestBuilder,
        optional: bool,
    ) -> Result<Option<Value>, String> {
        let response = request
            .timeout(Duration::from_secs(5))
            .send()
            .await
            .map_err(|e| e.to_string())?;
        if optional && response.status() == 404 {
            return Ok(None);
        }
        let response = response.error_for_status().map_err(|e| e.to_string())?;
        response.json().await.map(Some).map_err(|e| e.to_string())
    }
    async fn emotes(&self, provider: usize, channel: &str, enabled: bool) -> (Map, Vec<String>) {
        if !enabled {
            return (Map::new(), Vec::new());
        }
        let (global, user) = match provider {
            0 => (
                "emotes/global".to_string(),
                format!("users/twitch/{channel}"),
            ),
            1 => ("set/global".to_string(), format!("room/id/{channel}")),
            _ => (
                "emote-sets/global".to_string(),
                format!("users/twitch/{channel}"),
            ),
        };
        let mut map = Map::new();
        let mut errors = Vec::new();
        for (path, optional) in [(global, false), (user, true)] {
            let request = self.http.get(format!("{}/{path}", self.bases[provider]));
            match self.fetch(request, optional).await {
                Ok(Some(value)) => {
                    if let Err(error) = parse_emotes(&mut map, provider, &value, optional) {
                        errors.push(format!("{}: {error}", ["BTTV", "FFZ", "7TV"][provider]));
                    }
                }
                Ok(None) => {}
                Err(error) => errors.push(format!("{}: {error}", ["BTTV", "FFZ", "7TV"][provider])),
            }
        }
        (map, errors)
    }
    async fn badges(
        &self,
        helix: &TwitchHelixClient,
        channel: Option<&str>,
    ) -> (BadgeDefinitions, Vec<String>) {
        let endpoint = if channel.is_some() {
            "chat/badges"
        } else {
            "chat/badges/global"
        };
        let mut request = helix
            .http
            .get(format!("{}{endpoint}", helix.helix_base))
            .bearer_auth(&helix.access_token)
            .header("Client-Id", &helix.client_id);
        if let Some(channel) = channel {
            request = request.query(&[("broadcaster_id", channel)]);
        }
        let result = self
            .fetch(request, false)
            .await
            .and_then(|value| parse_badges(&value.unwrap_or(Value::Null)));
        match result {
            Ok(map) => (map, vec![]),
            Err(error) => (
                BadgeDefinitions::default(),
                vec![format!("Twitch {endpoint}: {error}")],
            ),
        }
    }
    pub async fn refresh(
        &self,
        helix: &TwitchHelixClient,
        channel: &str,
        settings: &OverlayChatSettings,
    ) -> ChatCatalogStatus {
        {
            let mut data = self.data.write().unwrap();
            if data.status.channel_id != channel {
                *data = Snapshot::default();
                data.status.channel_id = channel.into();
            }
        }
        // Each service is independent. A failed emote provider must not block badges or chat.
        let (bttv, ffz, seven, global, badges) = tokio::join!(
            self.emotes(0, channel, settings.enable_bttv),
            self.emotes(1, channel, settings.enable_ffz),
            self.emotes(2, channel, settings.enable_seven_tv),
            self.badges(helix, None),
            self.badges(helix, Some(channel))
        );
        let mut data = self.data.write().unwrap();
        let mut errors = vec![];
        update(&mut data.bttv, bttv.0, &bttv.1);
        errors.extend(bttv.1);
        update(&mut data.ffz, ffz.0, &ffz.1);
        errors.extend(ffz.1);
        update(&mut data.seven_tv, seven.0, &seven.1);
        errors.extend(seven.1);
        if global.1.is_empty() {
            data.global_badges = global.0;
        }
        errors.extend(global.1);
        if badges.1.is_empty() {
            data.channel_badges = badges.0;
        }
        errors.extend(badges.1);
        let mut emotes = data.bttv.clone();
        emotes.extend(data.ffz.clone());
        emotes.extend(data.seven_tv.clone());
        let mut badges = data.global_badges.versions.clone();
        badges.extend(data.channel_badges.versions.clone());
        data.status = ChatCatalogStatus {
            channel_id: channel.into(),
            emotes: emotes.len(),
            badges: badges.len(),
            errors,
            updated_at: Some(chrono::Utc::now().to_rfc3339()),
        };
        data.status.clone()
    }
    pub fn enrich(&self, event: &mut TwitchEvent, settings: &OverlayChatSettings) {
        if event.event_type != "channel.chat.message" {
            return;
        }
        let data = self.data.read().unwrap();
        let same_channel = event
            .data
            .get("broadcaster_user_id")
            .is_some_and(|id| id == &data.status.channel_id);
        let mut emotes = Map::new();
        if same_channel {
            if settings.enable_bttv {
                emotes.extend(data.bttv.clone());
            }
            if settings.enable_ffz {
                emotes.extend(data.ffz.clone());
            }
            if settings.enable_seven_tv {
                emotes.extend(data.seven_tv.clone());
            }
        }
        let raw: Value = event
            .data
            .get("message")
            .and_then(|v| serde_json::from_str(v).ok())
            .unwrap_or(Value::Null);
        let mut parts = Vec::new();
        if let Some(fragments) = raw["fragments"].as_array().filter(|f| !f.is_empty()) {
            for fragment in fragments {
                let text = fragment["text"].as_str().unwrap_or("");
                match fragment["type"].as_str().unwrap_or("text") {
                    "emote" => {
                        if let Some(id) = fragment["emote"]["id"].as_str().filter(|id| {
                            !id.is_empty()
                                && id.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'_')
                        }) {
                            parts.push(json!({"type":"emote","text":text,"provider":"twitch","url":format!("https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/2.0")}));
                        } else {
                            append_text(&mut parts, text);
                        }
                    }
                    "mention" | "cheermote" => append_text(&mut parts, text),
                    _ => tokenize(&mut parts, text, &emotes),
                }
            }
        } else {
            tokenize(
                &mut parts,
                event.data.get("text").map(String::as_str).unwrap_or(""),
                &emotes,
            );
        }
        event
            .data
            .insert("parts".into(), serde_json::to_string(&parts).unwrap());
        let ids: Value = event
            .data
            .get("badgeIds")
            .and_then(|v| serde_json::from_str(v).ok())
            .unwrap_or(Value::Null);
        let channel_badges = same_channel.then_some(&data.channel_badges);
        let mut resolved = vec![];
        for badge in ids.as_array().into_iter().flatten() {
            let set = badge["set_id"].as_str().unwrap_or("");
            let id = badge["id"].as_str().unwrap_or("");
            let key = format!("{}/{}", set.to_lowercase(), id.to_lowercase());
            let found = channel_badges
                .and_then(|badges| badges.versions.get(&key))
                .or_else(|| data.global_badges.versions.get(&key))
                .or_else(|| {
                    channel_badges.and_then(|badges| badges.by_set.get(&set.to_lowercase()))
                })
                .or_else(|| data.global_badges.by_set.get(&set.to_lowercase()))
                .cloned()
                .or_else(|| fallback_badge(set));
            if let Some(mut found) = found {
                found["setId"] = json!(set);
                found["id"] = json!(id);
                resolved.push(found);
            }
        }
        event
            .data
            .insert("badges".into(), serde_json::to_string(&resolved).unwrap());
    }
}
fn update(target: &mut Map, map: Map, errors: &[String]) {
    if errors.is_empty() {
        *target = map;
    } else {
        target.extend(map);
    }
}
fn append_text(parts: &mut Vec<Value>, text: &str) {
    if text.is_empty() {
        return;
    }
    if let Some(last) = parts.last_mut().filter(|p| p["type"] == "text") {
        let combined = format!("{}{text}", last["text"].as_str().unwrap_or(""));
        last["text"] = json!(combined);
    } else {
        parts.push(json!({"type":"text","text":text}));
    }
}
fn tokenize(parts: &mut Vec<Value>, text: &str, emotes: &Map) {
    let mut start = 0;
    let mut whitespace = None;
    for (index, ch) in text.char_indices() {
        let current = ch.is_whitespace();
        if whitespace.is_some_and(|last| last != current) {
            append_token(parts, &text[start..index], emotes);
            start = index;
        }
        whitespace = Some(current);
    }
    append_token(parts, &text[start..], emotes);
}
fn append_token(parts: &mut Vec<Value>, token: &str, emotes: &Map) {
    if let Some(emote) = emotes.get(token) {
        parts.push(emote.clone());
    } else {
        append_text(parts, token);
    }
}
fn image_url(raw: &str) -> Option<String> {
    let url = if raw.starts_with("//") {
        format!("https:{raw}")
    } else {
        raw.into()
    };
    let parsed = url::Url::parse(&url).ok()?;
    (matches!(parsed.scheme(), "http" | "https") && parsed.host_str().is_some()).then_some(url)
}
fn emote(map: &mut Map, code: &str, url: &str, provider: &str) {
    if !code.trim().is_empty() {
        if let Some(url) = image_url(url) {
            map.insert(
                code.into(),
                json!({"type":"emote","text":code,"url":url,"provider":provider}),
            );
        }
    }
}
fn parse_emotes(
    map: &mut Map,
    provider: usize,
    value: &Value,
    channel: bool,
) -> Result<(), String> {
    match provider {
        0 => {
            let lists = if channel {
                vec![&value["channelEmotes"], &value["sharedEmotes"]]
            } else {
                vec![value]
            };
            if !lists.iter().any(|v| v.is_array()) {
                return Err("Ungültiger Emote-Katalog".into());
            }
            for entry in lists.into_iter().filter_map(Value::as_array).flatten() {
                if let (Some(id), Some(code)) = (
                    entry["id"].as_str().filter(|id| !id.is_empty()),
                    entry["code"].as_str(),
                ) {
                    emote(
                        map,
                        code,
                        &format!("https://cdn.betterttv.net/emote/{id}/2x.webp"),
                        "bttv",
                    );
                }
            }
        }
        1 => {
            let sets = value["sets"]
                .as_object()
                .ok_or("Ungültiger Emote-Katalog")?;
            for entry in sets
                .values()
                .filter_map(|set| set["emoticons"].as_array())
                .flatten()
            {
                if let (Some(code), Some(url)) = (
                    entry["name"].as_str(),
                    entry["urls"]["2"]
                        .as_str()
                        .or_else(|| entry["urls"]["1"].as_str()),
                ) {
                    emote(map, code, url, "ffz");
                }
            }
        }
        _ => {
            let set = if channel { &value["emote_set"] } else { value };
            // A user without an emote set is a valid empty channel catalog.
            if channel && set.is_null() {
                return Ok(());
            }
            let entries = set["emotes"].as_array().ok_or("Ungültiger Emote-Katalog")?;
            for entry in entries {
                let host = &entry["data"]["host"];
                if let (Some(code), Some(url), Some(files)) = (
                    entry["name"].as_str(),
                    host["url"].as_str(),
                    host["files"].as_array(),
                ) {
                    let names: Vec<_> = files
                        .iter()
                        .filter_map(|f| f["name"].as_str().filter(|s| !s.is_empty()))
                        .collect();
                    if let Some(name) = names
                        .iter()
                        .find(|n| {
                            n.to_ascii_lowercase().contains("2x")
                                && n.to_ascii_lowercase().ends_with(".webp")
                        })
                        .or_else(|| names.first())
                    {
                        emote(
                            map,
                            code,
                            &format!("{}/{name}", url.trim_end_matches('/')),
                            "7tv",
                        );
                    }
                }
            }
        }
    }
    Ok(())
}
fn parse_badges(value: &Value) -> Result<BadgeDefinitions, String> {
    let mut map = BadgeDefinitions::default();
    for set in value["data"].as_array().ok_or("Ungültiger Badge-Katalog")? {
        let Some(set_id) = set["set_id"].as_str().filter(|s| !s.is_empty()) else {
            continue;
        };
        for badge in set["versions"].as_array().into_iter().flatten() {
            if let (Some(id), Some(url)) = (
                badge["id"].as_str().filter(|s| !s.is_empty()),
                badge["image_url_2x"]
                    .as_str()
                    .filter(|s| !s.trim().is_empty())
                    .or_else(|| badge["image_url_1x"].as_str())
                    .and_then(image_url),
            ) {
                let title = badge["title"]
                    .as_str()
                    .filter(|s| !s.is_empty())
                    .unwrap_or(set_id);
                let definition = json!({"setId":set_id,"id":id,"url":url,"title":title});
                map.versions.insert(
                    format!("{}/{}", set_id.to_lowercase(), id.to_lowercase()),
                    definition.clone(),
                );
                map.by_set.insert(set_id.to_lowercase(), definition);
            }
        }
    }
    Ok(map)
}
fn fallback_badge(set: &str) -> Option<Value> {
    // Same offline fallback definitions as the C# ChatBadgeCatalog.
    let (id, title) = match set.to_ascii_lowercase().as_str() {
        "broadcaster" => ("5527c58c-fb7d-422d-b71b-f309dcb85b62", "Broadcaster"),
        "moderator" => ("3267646d-33f0-4b17-b3df-f923a41db1d0", "Moderator"),
        "vip" => ("b817aba4-fad8-49e2-b88a-7cc724473d84", "VIP"),
        "subscriber" => ("5d9f2208-5dd8-11e7-8513-2ff4adfae661", "Subscriber"),
        "founder" => ("511b78a9-ab37-472f-9561-314f1bd5d137", "Founder"),
        "premium" => ("a1dd5073-19c3-4911-8cb4-c464a7bc1510", "Prime Gaming"),
        "partner" => ("d12a2e27-16f6-41d0-ab77-b780518f00a3", "Verified"),
        "staff" => ("d97c37bd-a6f5-4c38-8f57-4e4bef88af34", "Staff"),
        "admin" => ("9ef7e029-4ccd-4e57-8b9a-7b4f57b40c07", "Admin"),
        "global_mod" => ("9384cfc3-b2d1-412f-8bdc-a8bc1538c7d0", "Global Mod"),
        "artist-badge" => ("4300a3ff-7b9f-39d9-a8b7-85a9c0d0d4a9", "Artist"),
        "predictions" => ("e33d8b46-f63b-4e67-996d-4a7dce66ad0d", "Predictions"),
        _ => return None,
    };
    Some(json!({"url":format!("https://static-cdn.jtvnw.net/badges/v1/{id}/2"),"title":title}))
}
