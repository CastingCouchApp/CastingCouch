use super::{TwitchAction, TwitchClient, TwitchHelixClient, TwitchHelixUser};
use crate::{ModuleError, ModuleResult};
use serde::Serialize;
use serde_json::{json, Value};
use std::{
    collections::HashSet,
    time::{Duration, Instant},
};
use tokio::sync::Mutex;

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RaidSuggestion {
    pub login: String,
    pub display_name: String,
    pub is_live: bool,
    pub source_label: String,
}
#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RaidSuggestions {
    pub suggestions: Vec<RaidSuggestion>,
    pub warnings: Vec<String>,
}
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RaidTarget {
    pub id: String,
    pub login: String,
    pub display_name: String,
    pub profile_image_url: String,
    pub channel_url: String,
    pub is_online: bool,
    pub category: String,
    pub title: String,
    pub viewer_count: u64,
    pub started_at: Option<String>,
}
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RaidStarted {
    pub target: RaidTarget,
    pub response: Value,
    pub warnings: Vec<String>,
}

pub fn normalize_raid_channel(login: &str) -> String {
    login.trim().trim_start_matches('@').to_string()
}
pub fn normalize_raid_channels(channels: &[String]) -> Vec<String> {
    let mut seen = HashSet::new();
    channels
        .iter()
        .map(|s| normalize_raid_channel(s))
        .filter(|s| !s.is_empty() && seen.insert(s.to_lowercase()))
        .collect()
}
pub fn remember_raid_channel(channels: &[String], login: &str) -> Vec<String> {
    let login = normalize_raid_channel(login);
    if login.is_empty() {
        return normalize_raid_channels(channels);
    }
    normalize_raid_channels(&[vec![login], channels.to_vec()].concat())
        .into_iter()
        .take(40)
        .collect()
}
fn matches(login: &str, name: &str, query: &str) -> bool {
    login.to_lowercase().contains(query) || name.to_lowercase().contains(query)
}
pub fn build_raid_suggestions(
    recent: &[String],
    followed: &[RaidSuggestion],
    live: &[RaidSuggestion],
    searched: &[RaidSuggestion],
    probe: &[RaidSuggestion],
    query: &str,
    maximum: usize,
) -> Vec<RaidSuggestion> {
    let query = normalize_raid_channel(query).to_lowercase();
    let mut lookup = probe.to_vec();
    for item in live.iter().chain(searched.iter().filter(|s| s.is_live)) {
        if !lookup
            .iter()
            .any(|s| s.login.eq_ignore_ascii_case(&item.login))
        {
            lookup.push(item.clone());
        }
    }
    let mut recent: Vec<_> = normalize_raid_channels(recent)
        .into_iter()
        .filter(|s| matches(s, s, &query))
        .map(|login| {
            let live = lookup.iter().find(|s| s.login.eq_ignore_ascii_case(&login));
            RaidSuggestion {
                display_name: live.map_or_else(|| login.clone(), |s| s.display_name.clone()),
                login,
                is_live: live.is_some(),
                source_label: "Zuletzt".into(),
            }
        })
        .collect();
    recent.sort_by_key(|s| !s.is_live);
    let live = live
        .iter()
        .chain(searched.iter().filter(|s| s.is_live))
        .chain(lookup.iter())
        .filter(|s| matches(&s.login, &s.display_name, &query))
        .map(|s| RaidSuggestion {
            is_live: true,
            source_label: "Live".into(),
            ..s.clone()
        });
    let offline = followed
        .iter()
        .filter(|s| matches(&s.login, &s.display_name, &query))
        .chain(searched.iter().filter(|s| !s.is_live))
        .map(|s| RaidSuggestion {
            is_live: false,
            ..s.clone()
        });
    let mut seen = HashSet::new();
    recent
        .into_iter()
        .chain(live)
        .chain(offline)
        .filter(|s| seen.insert(s.login.to_lowercase()))
        .take(maximum.max(1))
        .collect()
}
fn probe_logins(recent: &[String], followed: &[RaidSuggestion], query: &str) -> Vec<String> {
    let query = normalize_raid_channel(query).to_lowercase();
    normalize_raid_channels(
        &recent
            .iter()
            .filter(|s| matches(s, s, &query))
            .cloned()
            .chain(
                followed
                    .iter()
                    .filter(|s| matches(&s.login, &s.display_name, &query))
                    .map(|s| s.login.clone()),
            )
            .collect::<Vec<_>>(),
    )
    .into_iter()
    .take(80)
    .collect()
}
fn items(value: &Value, kind: &str) -> Vec<RaidSuggestion> {
    value["data"]
        .as_array()
        .into_iter()
        .flatten()
        .filter_map(|item| {
            let (login, name, live, source) = match kind {
                "followed" => (
                    item["broadcaster_login"].as_str(),
                    item["broadcaster_name"].as_str(),
                    false,
                    "Gefolgt",
                ),
                "search" => (
                    item["broadcaster_login"].as_str(),
                    item["display_name"].as_str(),
                    item["is_live"].as_bool().unwrap_or(false),
                    "Suche",
                ),
                _ => (
                    item["user_login"].as_str(),
                    item["user_name"].as_str(),
                    true,
                    "Live",
                ),
            };
            let login = normalize_raid_channel(login?);
            if login.is_empty() {
                return None;
            }
            Some(RaidSuggestion {
                display_name: name.unwrap_or(&login).into(),
                login,
                is_live: live,
                source_label: source.into(),
            })
        })
        .collect()
}
impl TwitchHelixClient {
    async fn followed_page_set(&self, user: &str, live: bool) -> ModuleResult<Vec<RaidSuggestion>> {
        let mut result = Vec::new();
        let mut after = None;
        let mut seen = HashSet::new();
        loop {
            let response = self
                .community_get(
                    if live {
                        "streams/followed"
                    } else {
                        "channels/followed"
                    },
                    vec![
                        ("user_id".into(), user.into()),
                        ("first".into(), "100".into()),
                    ],
                    after,
                )
                .await?;
            result.extend(items(&response, if live { "live" } else { "followed" }));
            let cursor = response
                .pointer("/pagination/cursor")
                .and_then(Value::as_str)
                .unwrap_or("");
            if cursor.is_empty() || result.len() >= if live { 100 } else { 300 } {
                break;
            }
            if !seen.insert(cursor.to_string()) {
                return Err(ModuleError::Message(
                    "Twitch liefert eine wiederholte Pagination für gefolgte Kanäle.".into(),
                ));
            }
            after = Some(cursor.into());
        }
        result.truncate(if live { 100 } else { 300 });
        Ok(result)
    }
    async fn finish_suggestions(
        &self,
        recent: &[String],
        followed: &[RaidSuggestion],
        live: &[RaidSuggestion],
        query: &str,
        mut warnings: Vec<String>,
    ) -> RaidSuggestions {
        let query = normalize_raid_channel(query);
        let mut searched = vec![];
        let mut probe = vec![];
        if query.chars().count() >= 2 {
            match self
                .community_get(
                    "search/channels",
                    vec![
                        ("query".into(), query.clone()),
                        ("first".into(), "20".into()),
                    ],
                    None,
                )
                .await
            {
                Ok(value) => searched = items(&value, "search"),
                Err(error) => warnings.push(format!("Kanalsuche: {error}")),
            }
        }
        let logins = probe_logins(recent, followed, &query);
        if !logins.is_empty() {
            match self
                .community_get(
                    "streams",
                    logins
                        .into_iter()
                        .map(|s| ("user_login".into(), s))
                        .collect(),
                    None,
                )
                .await
            {
                Ok(value) => probe = items(&value, "live"),
                Err(error) => warnings.push(format!("Live-Status: {error}")),
            }
        }
        RaidSuggestions {
            suggestions: build_raid_suggestions(
                recent, followed, live, &searched, &probe, &query, 25,
            ),
            warnings,
        }
    }
    pub async fn raid_suggestions(
        &self,
        user: &str,
        recent: &[String],
        query: &str,
    ) -> RaidSuggestions {
        let mut warnings = vec![];
        let followed = self
            .followed_page_set(user, false)
            .await
            .unwrap_or_else(|e| {
                warnings.push(format!("Gefolgte Kanäle: {e}"));
                vec![]
            });
        let live = self
            .followed_page_set(user, true)
            .await
            .unwrap_or_else(|e| {
                warnings.push(format!("Gefolgte Live-Kanäle: {e}"));
                vec![]
            });
        self.finish_suggestions(recent, &followed, &live, query, warnings)
            .await
    }
    pub async fn raid_target(&self, login: &str) -> ModuleResult<Option<RaidTarget>> {
        let login = checked_login(login)?;
        let Some(user) = self.get_user_by_login(&login).await? else {
            return Ok(None);
        };
        Ok(Some(self.target_for_user(user).await?))
    }
    async fn target_for_user(&self, user: TwitchHelixUser) -> ModuleResult<RaidTarget> {
        let response = self
            .community_get(
                "streams",
                vec![
                    ("user_id".into(), user.id.clone()),
                    ("first".into(), "1".into()),
                ],
                None,
            )
            .await?;
        let stream = response["data"].as_array().and_then(|a| a.first());
        Ok(RaidTarget {
            id: user.id,
            channel_url: format!("https://www.twitch.tv/{}", user.login),
            login: user.login,
            display_name: user.display_name,
            profile_image_url: user.profile_image_url,
            is_online: stream.is_some(),
            category: stream
                .and_then(|s| s["game_name"].as_str())
                .unwrap_or("Offline")
                .into(),
            title: stream
                .and_then(|s| s["title"].as_str())
                .unwrap_or("")
                .into(),
            viewer_count: stream.and_then(|s| s["viewer_count"].as_u64()).unwrap_or(0),
            started_at: stream
                .and_then(|s| s["started_at"].as_str())
                .map(str::to_string),
        })
    }
    pub async fn start_raid(
        &self,
        broadcaster: &str,
        user: &str,
        login: &str,
        echo_chat: bool,
    ) -> ModuleResult<RaidStarted> {
        // Resolve the user separately so self-raids do not query the stream or write anything.
        let login = checked_login(login)?;
        let user_info = self
            .get_user_by_login(&login)
            .await?
            .ok_or_else(|| ModuleError::Message("Raid-Kanal nicht gefunden.".into()))?;
        if user_info.id == broadcaster {
            return Err(ModuleError::Message(
                "Ein Raid zum eigenen Kanal ist nicht möglich.".into(),
            ));
        }
        let target = self.target_for_user(user_info).await?;
        if !target.is_online {
            return Err(ModuleError::Message("Das Raid-Ziel ist offline.".into()));
        }
        let response = self
            .community_action(
                TwitchAction::Raid {
                    id: target.id.clone(),
                },
                broadcaster,
                user,
            )
            .await
            .map_err(|error| {
                // Twitch may have accepted the POST before a transport/5xx/body error.
                if matches!(error, ModuleError::Http(_))
                    || error.to_string().contains("Twitch API 5")
                    || !error.to_string().starts_with("Twitch API ")
                {
                    ModuleError::Message(format!(
                        "Raid-Ausgang unklar; zuerst in Twitch prüfen oder abbrechen: {error}"
                    ))
                } else {
                    error
                }
            })?;
        if !response["data"]
            .as_array()
            .is_some_and(|data| !data.is_empty())
        {
            return Err(ModuleError::Message("Raid-Ausgang unklar; Twitch hat keine Startbestätigung geliefert. Zuerst in Twitch prüfen oder abbrechen.".into()));
        }
        let mut warnings = vec![];
        if echo_chat {
            if let Err(error) = self
                .community_action(
                    TwitchAction::SendChat {
                        message: format!("/raid {}", target.login),
                    },
                    broadcaster,
                    user,
                )
                .await
            {
                warnings.push(format!(
                    "Raid gestartet; Chat-Bestätigung fehlgeschlagen: {error}"
                ));
            }
        }
        Ok(RaidStarted {
            target,
            response,
            warnings,
        })
    }
    pub async fn cancel_raid(&self, broadcaster: &str) -> ModuleResult<()> {
        self.community_action(TwitchAction::CancelRaid, broadcaster, "")
            .await?;
        Ok(())
    }
}
pub fn checked_raid_login(login: &str) -> ModuleResult<String> {
    checked_login(login)
}
fn checked_login(login: &str) -> ModuleResult<String> {
    let login = normalize_raid_channel(login);
    if login.is_empty()
        || login.len() > 25
        || !login
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'_')
    {
        return Err(ModuleError::Message("Ungültiger Twitch-Kanal.".into()));
    }
    Ok(login)
}

#[derive(Default)]
struct Cache {
    key: String,
    followed: Option<(Instant, Vec<RaidSuggestion>)>,
    live: Option<(Instant, Vec<RaidSuggestion>)>,
}
#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RaidState {
    pub requested_target: Option<String>,
    pub requested_at: Option<String>,
    pub last_error: Option<String>,
}
#[derive(Default)]
pub(super) struct CommunityState {
    cache: Mutex<Cache>,
    raid: Mutex<RaidSession>,
}
#[derive(Default)]
struct RaidSession {
    key: String,
    state: RaidState,
}
impl TwitchClient {
    pub async fn community_suggestions(
        &self,
        client: &str,
        channel: &str,
        recent: &[String],
        query: &str,
        force: bool,
    ) -> ModuleResult<RaidSuggestions> {
        let (helix, _, user) = self.operation_client(client, channel).await?;
        let mut cache = self.community.cache.lock().await;
        let key = json!([client, channel, user]).to_string();
        if cache.key != key {
            *cache = Cache {
                key,
                ..Default::default()
            };
        }
        let mut warnings = vec![];
        if force
            || cache
                .followed
                .as_ref()
                .is_none_or(|(at, _)| at.elapsed() >= Duration::from_secs(600))
        {
            match helix.followed_page_set(&user, false).await {
                Ok(items) => cache.followed = Some((Instant::now(), items)),
                Err(e) => warnings.push(format!("Gefolgte Kanäle: {e}")),
            }
        }
        if force
            || cache
                .live
                .as_ref()
                .is_none_or(|(at, _)| at.elapsed() >= Duration::from_secs(120))
        {
            match helix.followed_page_set(&user, true).await {
                Ok(items) => cache.live = Some((Instant::now(), items)),
                Err(e) => warnings.push(format!("Gefolgte Live-Kanäle: {e}")),
            }
        }
        let followed = cache
            .followed
            .as_ref()
            .map(|(_, v)| v.clone())
            .unwrap_or_default();
        let live = cache
            .live
            .as_ref()
            .map(|(_, v)| v.clone())
            .unwrap_or_default();
        drop(cache);
        Ok(helix
            .finish_suggestions(recent, &followed, &live, query, warnings)
            .await)
    }
    pub async fn community_target(
        &self,
        client: &str,
        channel: &str,
        login: &str,
    ) -> ModuleResult<Option<RaidTarget>> {
        checked_login(login)?;
        let (helix, _, _) = self.operation_client(client, channel).await?;
        helix.raid_target(login).await
    }
    pub async fn community_start(
        &self,
        client: &str,
        channel: &str,
        login: &str,
        echo: bool,
    ) -> ModuleResult<RaidStarted> {
        checked_login(login)?;
        let mut session = self.community.raid.lock().await;
        let (helix, broadcaster, user) = self.operation_client(client, channel).await?;
        let key = json!([client, channel, user]).to_string();
        if session.key != key {
            *session = RaidSession {
                key,
                ..Default::default()
            };
        }
        let state = &mut session.state;
        if state.requested_target.is_some() {
            return Err(ModuleError::Message("Ein Raid wurde bereits angefordert. Zuerst abbrechen oder den abgeschlossenen Raid bestätigen.".into()));
        }
        match helix.start_raid(&broadcaster, &user, login, echo).await {
            Ok(result) => {
                state.requested_target = Some(result.target.login.clone());
                state.requested_at = Some(chrono::Utc::now().to_rfc3339());
                state.last_error = None;
                Ok(result)
            }
            Err(error) => {
                if error.to_string().starts_with("Raid-Ausgang unklar") {
                    state.requested_target = Some(normalize_raid_channel(login));
                    state.requested_at = Some(chrono::Utc::now().to_rfc3339());
                }
                state.last_error = Some(error.to_string());
                Err(error)
            }
        }
    }
    pub async fn community_cancel(&self, client: &str, channel: &str) -> ModuleResult<()> {
        let mut session = self.community.raid.lock().await;
        let (helix, broadcaster, user) = self.operation_client(client, channel).await?;
        let key = json!([client, channel, user]).to_string();
        if session.key != key {
            *session = RaidSession {
                key,
                ..Default::default()
            };
        }
        let state = &mut session.state;
        match helix.cancel_raid(&broadcaster).await {
            Ok(()) => {
                *state = RaidState::default();
                Ok(())
            }
            Err(error) => {
                state.last_error = Some(error.to_string());
                Err(error)
            }
        }
    }
    pub async fn community_raid_state(&self, client: &str, channel: &str) -> RaidState {
        let user = self.current_user().await.map(|u| u.id).unwrap_or_default();
        let session = self.community.raid.lock().await;
        if session.key == json!([client, channel, user]).to_string() {
            session.state.clone()
        } else {
            RaidState::default()
        }
    }
    pub async fn acknowledge_raid(&self) {
        self.community.raid.lock().await.state = RaidState::default();
    }
}
