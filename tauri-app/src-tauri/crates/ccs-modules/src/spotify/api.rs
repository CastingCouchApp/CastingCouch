use crate::{ModuleError, ModuleResult};
use serde::Deserialize;

use super::tokens::{NowPlaying, SpotifyUser};

pub const API_BASE_URL: &str = "https://api.spotify.com/v1/";

#[derive(Clone)]
pub struct SpotifyApiClient {
    http: reqwest::Client,
    api_base: String,
}

impl SpotifyApiClient {
    pub fn new() -> Self {
        Self {
            http: reqwest::Client::new(),
            api_base: API_BASE_URL.into(),
        }
    }

    pub fn with_base_url(api_base: impl Into<String>) -> Self {
        Self {
            http: reqwest::Client::new(),
            api_base: normalize_base(api_base.into()),
        }
    }

    pub fn currently_playing_url(&self) -> String {
        format!("{}me/player/currently-playing", self.api_base)
    }

    pub fn me_url(&self) -> String {
        format!("{}me", self.api_base)
    }

    pub async fn get_current_user(&self, access_token: &str) -> ModuleResult<SpotifyUser> {
        let response = self
            .http
            .get(self.me_url())
            .header("Authorization", format!("Bearer {access_token}"))
            .send()
            .await?;
        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(map_api_error(status.as_u16(), &body));
        }
        let parsed: MeResponse = serde_json::from_str(&body)
            .map_err(|e| ModuleError::Message(format!("Spotify-Benutzerantwort ungültig: {e}")))?;
        let display_name = if parsed.display_name.trim().is_empty() {
            parsed.id.clone()
        } else {
            parsed.display_name
        };
        Ok(SpotifyUser {
            id: parsed.id,
            display_name,
        })
    }

    pub async fn get_currently_playing(&self, access_token: &str) -> ModuleResult<NowPlaying> {
        let response = self
            .http
            .get(self.currently_playing_url())
            .header("Authorization", format!("Bearer {access_token}"))
            .send()
            .await?;
        let status = response.status();
        if status.as_u16() == 204 {
            return Ok(NowPlaying::default());
        }
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(map_api_error(status.as_u16(), &body));
        }
        if body.trim().is_empty() {
            return Ok(NowPlaying::default());
        }
        map_currently_playing(&body)
    }
}

impl Default for SpotifyApiClient {
    fn default() -> Self {
        Self::new()
    }
}

pub fn map_currently_playing(body: &str) -> ModuleResult<NowPlaying> {
    let parsed: CurrentlyPlayingResponse = serde_json::from_str(body)
        .map_err(|e| ModuleError::Message(format!("Spotify currently-playing ungültig: {e}")))?;
    let item = match parsed.item {
        Some(item)
            if parsed.currently_playing_type.eq_ignore_ascii_case("track")
                || item.item_type.eq_ignore_ascii_case("track")
                || (parsed.currently_playing_type.is_empty() && item.item_type.is_empty()) =>
        {
            item
        }
        _ => {
            return Ok(NowPlaying {
                title: String::new(),
                artist: String::new(),
                album: String::new(),
                is_playing: parsed.is_playing,
            })
        }
    };
    let artist = item
        .artists
        .iter()
        .map(|a| a.name.as_str())
        .filter(|n| !n.is_empty())
        .collect::<Vec<_>>()
        .join(", ");
    Ok(NowPlaying {
        title: item.name,
        artist,
        album: item.album.map(|a| a.name).unwrap_or_default(),
        is_playing: parsed.is_playing,
    })
}

fn normalize_base(base: String) -> String {
    if base.ends_with('/') {
        base
    } else {
        format!("{base}/")
    }
}

pub(crate) fn map_api_error(status: u16, body: &str) -> ModuleError {
    let message = parse_api_error_message(body);
    ModuleError::Message(format!("Spotify API {status}: {message}"))
}

fn parse_api_error_message(body: &str) -> String {
    #[derive(Deserialize)]
    struct Wrapper {
        #[serde(default)]
        error: Option<ErrorBody>,
        #[serde(default)]
        error_description: String,
        #[serde(default)]
        message: String,
    }
    #[derive(Deserialize)]
    struct ErrorBody {
        #[serde(default)]
        message: String,
        #[serde(default)]
        status: u16,
    }
    if let Ok(parsed) = serde_json::from_str::<Wrapper>(body) {
        if let Some(err) = parsed.error {
            if !err.message.is_empty() {
                return err.message;
            }
            if err.status != 0 {
                return format!("HTTP {}", err.status);
            }
        }
        if !parsed.error_description.is_empty() {
            return parsed.error_description;
        }
        if !parsed.message.is_empty() {
            return parsed.message;
        }
    }
    "Anfrage fehlgeschlagen.".into()
}

#[derive(Deserialize)]
struct MeResponse {
    #[serde(default)]
    id: String,
    #[serde(default)]
    display_name: String,
}

#[derive(Deserialize)]
struct CurrentlyPlayingResponse {
    #[serde(default)]
    is_playing: bool,
    #[serde(default)]
    currently_playing_type: String,
    #[serde(default)]
    item: Option<ItemResponse>,
}

#[derive(Deserialize)]
struct ItemResponse {
    #[serde(default)]
    name: String,
    #[serde(default, rename = "type")]
    item_type: String,
    #[serde(default)]
    artists: Vec<ArtistResponse>,
    #[serde(default)]
    album: Option<AlbumResponse>,
}

#[derive(Deserialize)]
struct ArtistResponse {
    #[serde(default)]
    name: String,
}

#[derive(Deserialize)]
struct AlbumResponse {
    #[serde(default)]
    name: String,
}
