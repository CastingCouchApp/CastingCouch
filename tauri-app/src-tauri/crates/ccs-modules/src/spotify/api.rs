use crate::{ModuleError, ModuleResult};
use serde::Deserialize;

use super::tokens::{NowPlaying, SpotifyUser};

pub const API_BASE_URL: &str = "https://api.spotify.com/v1/";

#[derive(Clone)]
pub struct SpotifyApiClient {
    pub(super) http: reqwest::Client,
    pub(super) api_base: String,
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
                ..Default::default()
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
        cover_url: item
            .album
            .as_ref()
            .and_then(|a| a.images.first())
            .map(|i| i.url.clone())
            .unwrap_or_default(),
        progress_ms: parsed
            .progress_ms
            .unwrap_or(0)
            .clamp(0, item.duration_ms.max(0)),
        duration_ms: item.duration_ms.max(0),
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
    progress_ms: Option<i64>,
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
    duration_ms: i64,
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
    images: Vec<ImageResponse>,
    #[serde(default)]
    name: String,
}

#[cfg(test)]
mod metadata_tests {
    #[test]
    fn overlay_metadata_includes_cover_and_clamped_progress() {
        let value=super::map_currently_playing(r#"{"is_playing":true,"progress_ms":9000,"item":{"type":"track","name":"Song","duration_ms":8000,"album":{"name":"Album","images":[{"url":"https://example.org/cover.png"}]},"artists":[{"name":"Artist"}]}}"#).unwrap();
        assert_eq!(value.cover_url, "https://example.org/cover.png");
        assert_eq!(value.duration_ms, 8000);
        assert_eq!(value.progress_ms, 8000);
    }
}

#[derive(Deserialize)]
struct ImageResponse {
    #[serde(default)]
    url: String,
}
