use crate::ModuleResult;
use serde::Deserialize;

use super::tokens::{TwitchDeviceCode, TwitchTokenSet, TwitchTokenValidation};

pub const OAUTH_DEVICE_URL: &str = "https://id.twitch.tv/oauth2/device";
pub const OAUTH_TOKEN_URL: &str = "https://id.twitch.tv/oauth2/token";
pub const OAUTH_VALIDATE_URL: &str = "https://id.twitch.tv/oauth2/validate";

#[derive(Clone)]
pub struct TwitchOAuthClient {
    http: reqwest::Client,
    device_url: String,
    token_url: String,
    validate_url: String,
}

impl Default for TwitchOAuthClient {
    fn default() -> Self {
        Self::new()
    }
}

impl TwitchOAuthClient {
    pub fn new() -> Self {
        Self {
            http: reqwest::Client::new(),
            device_url: OAUTH_DEVICE_URL.into(),
            token_url: OAUTH_TOKEN_URL.into(),
            validate_url: OAUTH_VALIDATE_URL.into(),
        }
    }

    pub fn with_base_urls(
        device_url: impl Into<String>,
        token_url: impl Into<String>,
        validate_url: impl Into<String>,
    ) -> Self {
        Self {
            http: reqwest::Client::new(),
            device_url: device_url.into(),
            token_url: token_url.into(),
            validate_url: validate_url.into(),
        }
    }

    pub fn validate_client_id(client_id: &str) -> ModuleResult<()> {
        let value = client_id.trim();
        if value.is_empty() {
            return Err(crate::ModuleError::Message(
                "Twitch Client-ID fehlt. Bitte unter Einstellungen → Twitch eine gültige Client-ID eintragen."
                    .into(),
            ));
        }
        let lower = value.to_ascii_lowercase();
        if value.len() < 20
            || lower.contains("your_client_id")
            || lower.contains("placeholder")
            || lower.contains("changeme")
        {
            return Err(crate::ModuleError::Message(
                "Twitch Client-ID ist ungültig. Bitte unter Einstellungen → Twitch eine gültige Client-ID deiner Twitch-Developer-App eintragen."
                    .into(),
            ));
        }
        Ok(())
    }

    pub async fn start_device_authorization(
        &self,
        client_id: &str,
        scopes: &[String],
    ) -> ModuleResult<TwitchDeviceCode> {
        Self::validate_client_id(client_id)?;
        let scopes_joined = scopes.join(" ");
        let response = self
            .http
            .post(&self.device_url)
            .form(&[("client_id", client_id), ("scopes", scopes_joined.as_str())])
            .send()
            .await?;
        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(map_http_error(status.as_u16(), &body));
        }
        let result: DeviceCodeResponse = serde_json::from_str(&body).map_err(|e| {
            crate::ModuleError::Message(format!("Twitch Device-Code-Antwort ungültig: {e}"))
        })?;
        Ok(TwitchDeviceCode {
            device_code: result.device_code,
            user_code: result.user_code,
            verification_uri: result.verification_uri,
            expires_in_seconds: result.expires_in,
            poll_interval_seconds: result.interval.max(1),
        })
    }

    pub async fn wait_for_device_authorization(
        &self,
        client_id: &str,
        device: &TwitchDeviceCode,
    ) -> ModuleResult<TwitchTokenSet> {
        Self::validate_client_id(client_id)?;
        let expires_at = std::time::Instant::now()
            + std::time::Duration::from_secs(device.expires_in_seconds.max(1) as u64);
        let mut interval =
            std::time::Duration::from_secs(device.poll_interval_seconds.max(1) as u64);

        while std::time::Instant::now() < expires_at {
            tokio::time::sleep(interval).await;

            let response = self
                .http
                .post(&self.token_url)
                .form(&[
                    ("client_id", client_id),
                    ("scopes", ""),
                    ("device_code", device.device_code.as_str()),
                    ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                ])
                .send()
                .await?;

            let status = response.status();
            let body = response.text().await.unwrap_or_default();

            if status.is_success() {
                return parse_token_response(&body);
            }

            let message = parse_error_message(&body);
            if status.as_u16() == 400 && message.eq_ignore_ascii_case("authorization_pending") {
                continue;
            }
            if status.as_u16() == 400 && message.eq_ignore_ascii_case("slow_down") {
                interval += std::time::Duration::from_secs(2);
                continue;
            }
            return Err(crate::ModuleError::Message(format!(
                "Twitch-Autorisierung fehlgeschlagen: {message}"
            )));
        }

        Err(crate::ModuleError::Message(
            "Der Twitch-Autorisierungscode ist abgelaufen.".into(),
        ))
    }

    pub async fn refresh(
        &self,
        client_id: &str,
        refresh_token: &str,
    ) -> ModuleResult<TwitchTokenSet> {
        Self::validate_client_id(client_id)?;
        if refresh_token.trim().is_empty() {
            return Err(crate::ModuleError::Message(
                "Twitch Refresh-Token fehlt.".into(),
            ));
        }
        let response = self
            .http
            .post(&self.token_url)
            .form(&[
                ("client_id", client_id),
                ("grant_type", "refresh_token"),
                ("refresh_token", refresh_token),
            ])
            .send()
            .await?;
        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(map_http_error(status.as_u16(), &body));
        }
        parse_token_response(&body)
    }

    pub async fn validate(&self, access_token: &str) -> ModuleResult<TwitchTokenValidation> {
        if access_token.trim().is_empty() {
            return Err(crate::ModuleError::Message(
                "Twitch Access-Token fehlt.".into(),
            ));
        }
        let response = self
            .http
            .get(&self.validate_url)
            .header("Authorization", format!("OAuth {access_token}"))
            .send()
            .await?;
        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(map_http_error(status.as_u16(), &body));
        }
        let result: ValidationResponse = serde_json::from_str(&body).map_err(|e| {
            crate::ModuleError::Message(format!("Twitch Tokenvalidierung ungültig: {e}"))
        })?;
        Ok(TwitchTokenValidation {
            client_id: result.client_id,
            login: result.login,
            user_id: result.user_id,
            scopes: result.scopes,
            expires_in_seconds: result.expires_in,
        })
    }
}

fn parse_token_response(body: &str) -> ModuleResult<TwitchTokenSet> {
    let token: TokenResponse = serde_json::from_str(body)
        .map_err(|e| crate::ModuleError::Message(format!("Twitch Token-Antwort ungültig: {e}")))?;
    Ok(TwitchTokenSet::from_oauth(
        token.access_token,
        token.refresh_token,
        token.expires_in,
        token.scope,
    ))
}

fn parse_error_message(body: &str) -> String {
    #[derive(Deserialize)]
    struct ErrBody {
        #[serde(default)]
        message: String,
    }
    serde_json::from_str::<ErrBody>(body)
        .map(|e| e.message)
        .unwrap_or_else(|_| body.to_string())
}

fn map_http_error(status: u16, body: &str) -> crate::ModuleError {
    let message = parse_error_message(body);
    if message.eq_ignore_ascii_case("invalid client") {
        return crate::ModuleError::Message(
            "Twitch Client-ID ist ungültig. Bitte unter Einstellungen → Twitch eine gültige Client-ID deiner Twitch-Developer-App eintragen."
                .into(),
        );
    }
    crate::ModuleError::Message(format!("Twitch HTTP {status}: {message}"))
}

#[derive(Deserialize)]
struct DeviceCodeResponse {
    device_code: String,
    user_code: String,
    verification_uri: String,
    expires_in: i32,
    #[serde(default)]
    interval: i32,
}

#[derive(Deserialize)]
struct TokenResponse {
    access_token: String,
    #[serde(default)]
    refresh_token: String,
    expires_in: i32,
    #[serde(default)]
    scope: Vec<String>,
}

#[derive(Deserialize)]
struct ValidationResponse {
    client_id: String,
    login: String,
    user_id: String,
    #[serde(default)]
    scopes: Vec<String>,
    expires_in: i32,
}
