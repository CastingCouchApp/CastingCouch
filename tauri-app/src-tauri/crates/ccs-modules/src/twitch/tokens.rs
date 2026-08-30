use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

/// PascalCase blob stored under `twitch.tokenSet` (WPF-compatible).
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "PascalCase")]
pub struct TwitchTokenSet {
    pub access_token: String,
    pub refresh_token: String,
    pub expires_in_seconds: i32,
    pub scopes: Vec<String>,
    pub obtained_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct TwitchDeviceCode {
    pub device_code: String,
    pub user_code: String,
    pub verification_uri: String,
    pub expires_in_seconds: i32,
    pub poll_interval_seconds: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct TwitchTokenValidation {
    pub client_id: String,
    pub login: String,
    pub user_id: String,
    pub scopes: Vec<String>,
    pub expires_in_seconds: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct TwitchHelixUser {
    pub id: String,
    pub login: String,
    pub display_name: String,
    #[serde(default)]
    pub profile_image_url: String,
}

impl TwitchTokenSet {
    pub fn from_oauth(
        access_token: String,
        refresh_token: String,
        expires_in: i32,
        scopes: Vec<String>,
    ) -> Self {
        Self {
            access_token,
            refresh_token,
            expires_in_seconds: expires_in,
            scopes,
            obtained_at: Utc::now(),
        }
    }
}
