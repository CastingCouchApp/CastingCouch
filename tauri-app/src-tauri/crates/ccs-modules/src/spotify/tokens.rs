use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

/// PascalCase blob stored under `spotify.tokenSet` (WPF-compatible).
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "PascalCase")]
pub struct SpotifyTokenSet {
    pub access_token: String,
    pub refresh_token: String,
    pub expires_in_seconds: i32,
    #[serde(default = "default_token_type")]
    pub token_type: String,
    #[serde(default)]
    pub scopes: Vec<String>,
    pub obtained_at: DateTime<Utc>,
}

fn default_token_type() -> String {
    "Bearer".into()
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq, Default)]
pub struct NowPlaying {
    pub title: String,
    pub artist: String,
    pub album: String,
    pub is_playing: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq, Default)]
pub struct SpotifyUser {
    pub id: String,
    pub display_name: String,
}

impl SpotifyTokenSet {
    pub fn from_oauth(
        access_token: String,
        refresh_token: String,
        expires_in: i32,
        token_type: String,
        scopes: Vec<String>,
    ) -> Self {
        Self {
            access_token,
            refresh_token,
            expires_in_seconds: expires_in,
            token_type: if token_type.is_empty() {
                "Bearer".into()
            } else {
                token_type
            },
            scopes,
            obtained_at: Utc::now(),
        }
    }

    /// Spotify access tokens are treated as expired 60s early (C# ExpiresAt).
    pub fn expires_at(&self) -> DateTime<Utc> {
        self.obtained_at + chrono::Duration::seconds(i64::from(self.expires_in_seconds.max(0)) - 60)
    }

    pub fn is_expired(&self) -> bool {
        Utc::now() >= self.expires_at()
    }
}
