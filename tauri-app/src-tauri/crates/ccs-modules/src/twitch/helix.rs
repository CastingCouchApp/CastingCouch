use crate::ModuleResult;
use serde::Deserialize;
use serde_json::{json, Value};

use super::tokens::TwitchHelixUser;

pub const HELIX_BASE_URL: &str = "https://api.twitch.tv/helix/";

#[derive(Clone)]
pub struct TwitchHelixClient {
    http: reqwest::Client,
    helix_base: String,
    client_id: String,
    access_token: String,
}

impl TwitchHelixClient {
    pub fn new(client_id: impl Into<String>, access_token: impl Into<String>) -> Self {
        Self {
            http: reqwest::Client::new(),
            helix_base: HELIX_BASE_URL.into(),
            client_id: client_id.into(),
            access_token: access_token.into(),
        }
    }

    pub fn with_base_url(
        helix_base: impl Into<String>,
        client_id: impl Into<String>,
        access_token: impl Into<String>,
    ) -> Self {
        Self {
            http: reqwest::Client::new(),
            helix_base: helix_base.into(),
            client_id: client_id.into(),
            access_token: access_token.into(),
        }
    }

    pub fn configure(&mut self, client_id: impl Into<String>, access_token: impl Into<String>) {
        self.client_id = client_id.into();
        self.access_token = access_token.into();
    }

    pub fn helix_users_url(login: Option<&str>) -> String {
        match login {
            Some(login) if !login.is_empty() => {
                let encoded = urlencoding_encode(login);
                format!("{HELIX_BASE_URL}users?login={encoded}")
            }
            _ => format!("{HELIX_BASE_URL}users"),
        }
    }

    pub async fn get_current_user(&self) -> ModuleResult<TwitchHelixUser> {
        self.get_user(None).await
    }

    pub async fn get_user_by_login(&self, login: &str) -> ModuleResult<Option<TwitchHelixUser>> {
        if login.trim().is_empty() {
            return Ok(None);
        }
        match self.get_user(Some(login)).await {
            Ok(user) => Ok(Some(user)),
            Err(crate::ModuleError::Message(msg)) if msg.contains("kein Benutzer") => Ok(None),
            Err(e) => Err(e),
        }
    }

    async fn get_user(&self, login: Option<&str>) -> ModuleResult<TwitchHelixUser> {
        if self.client_id.is_empty() || self.access_token.is_empty() {
            return Err(crate::ModuleError::Message(
                "Twitch API ist nicht konfiguriert.".into(),
            ));
        }

        let url = match login {
            Some(login) if !login.is_empty() => {
                let encoded = urlencoding_encode(login);
                format!("{}users?login={encoded}", self.helix_base)
            }
            _ => format!("{}users", self.helix_base),
        };

        let response = self
            .http
            .get(&url)
            .header("Authorization", format!("Bearer {}", self.access_token))
            .header("Client-Id", &self.client_id)
            .send()
            .await?;

        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            let message = parse_helix_error(&body);
            return Err(crate::ModuleError::Message(format!(
                "Twitch API {}: {message}",
                status.as_u16()
            )));
        }

        let parsed: HelixUsersResponse = serde_json::from_str(&body).map_err(|e| {
            crate::ModuleError::Message(format!("Twitch Helix /users ungültig: {e}"))
        })?;
        parsed
            .data
            .into_iter()
            .next()
            .map(|u| TwitchHelixUser {
                id: u.id,
                login: u.login,
                display_name: u.display_name,
                profile_image_url: u.profile_image_url.unwrap_or_default(),
            })
            .ok_or_else(|| {
                crate::ModuleError::Message("Twitch Helix /users lieferte keinen Benutzer.".into())
            })
    }

    pub async fn create_eventsub_subscription(
        &self,
        event_type: &str,
        version: &str,
        condition: Value,
        session_id: &str,
    ) -> ModuleResult<()> {
        if self.client_id.is_empty() || self.access_token.is_empty() {
            return Err(crate::ModuleError::Message(
                "Twitch API ist nicht konfiguriert.".into(),
            ));
        }

        let url = format!("{}eventsub/subscriptions", self.helix_base);
        let body = json!({
            "type": event_type,
            "version": version,
            "condition": condition,
            "transport": {
                "method": "websocket",
                "session_id": session_id
            }
        });

        let response = self
            .http
            .post(&url)
            .header("Authorization", format!("Bearer {}", self.access_token))
            .header("Client-Id", &self.client_id)
            .json(&body)
            .send()
            .await?;

        let status = response.status();
        let resp_body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            let message = parse_helix_error(&resp_body);
            return Err(crate::ModuleError::Message(format!(
                "Twitch API {}: {message}",
                status.as_u16()
            )));
        }
        Ok(())
    }
}

fn urlencoding_encode(value: &str) -> String {
    url::form_urlencoded::byte_serialize(value.as_bytes()).collect::<String>()
}

fn parse_helix_error(body: &str) -> String {
    #[derive(Deserialize)]
    struct ErrBody {
        #[serde(default)]
        message: String,
    }
    serde_json::from_str::<ErrBody>(body)
        .map(|e| {
            if e.message.is_empty() {
                body.to_string()
            } else {
                e.message
            }
        })
        .unwrap_or_else(|_| body.to_string())
}

#[derive(Deserialize)]
struct HelixUsersResponse {
    data: Vec<HelixUserDto>,
}

#[derive(Deserialize)]
struct HelixUserDto {
    id: String,
    login: String,
    display_name: String,
    #[serde(default)]
    profile_image_url: Option<String>,
}
