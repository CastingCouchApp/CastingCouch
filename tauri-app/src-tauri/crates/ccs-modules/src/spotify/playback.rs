use super::{api::SpotifyApiClient, SpotifyClient};
use crate::{ModuleError, ModuleResult};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(
    tag = "action",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum SpotifyAction {
    Play,
    Pause,
    Next,
    Previous,
    Volume { percent: u8 },
    Seek { position_ms: u32 },
    Shuffle { enabled: bool },
    Repeat { mode: String },
    Transfer { device_id: String },
    PlayTrack { uri: String },
    PlayPlaylist { uri: String },
    Queue { uri: String },
    SaveTrack { id: String },
    RemoveSavedTrack { id: String },
}

#[derive(Clone, Debug, Deserialize)]
#[serde(
    tag = "query",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum SpotifyQuery {
    Playback,
    Devices,
    Queue,
    Recent,
    Saved,
    Playlists,
    PlaylistTracks { id: String },
    Search { text: String },
}

struct Request {
    method: reqwest::Method,
    path: String,
    params: Vec<(String, String)>,
    body: Option<Value>,
}
impl Request {
    fn new(method: reqwest::Method, path: impl Into<String>) -> Self {
        Self {
            method,
            path: path.into(),
            params: vec![],
            body: None,
        }
    }
    fn param(mut self, name: &str, value: impl ToString) -> Self {
        self.params.push((name.into(), value.to_string()));
        self
    }
    fn body(mut self, value: Value) -> Self {
        self.body = Some(value);
        self
    }
}

fn valid_id(value: &str) -> ModuleResult<&str> {
    if !value.is_empty() && value.bytes().all(|b| b.is_ascii_alphanumeric()) {
        Ok(value)
    } else {
        Err(ModuleError::Message("Ungültige Spotify-ID".into()))
    }
}
impl SpotifyAction {
    fn request(&self) -> ModuleResult<Request> {
        use reqwest::Method;
        Ok(match self {
            Self::Play => Request::new(Method::PUT, "me/player/play"),
            Self::Pause => Request::new(Method::PUT, "me/player/pause"),
            Self::Next => Request::new(Method::POST, "me/player/next"),
            Self::Previous => Request::new(Method::POST, "me/player/previous"),
            Self::Volume { percent } if *percent <= 100 => {
                Request::new(Method::PUT, "me/player/volume").param("volume_percent", percent)
            }
            Self::Volume { .. } => {
                return Err(ModuleError::Message(
                    "Lautstärke muss zwischen 0 und 100 liegen".into(),
                ))
            }
            Self::Seek { position_ms } => {
                Request::new(Method::PUT, "me/player/seek").param("position_ms", position_ms)
            }
            Self::Shuffle { enabled } => {
                Request::new(Method::PUT, "me/player/shuffle").param("state", enabled)
            }
            Self::Repeat { mode } if ["off", "context", "track"].contains(&mode.as_str()) => {
                Request::new(Method::PUT, "me/player/repeat").param("state", mode)
            }
            Self::Repeat { .. } => {
                return Err(ModuleError::Message("Ungültiger Wiederholungsmodus".into()))
            }
            Self::Transfer { device_id } => Request::new(Method::PUT, "me/player")
                .body(json!({"device_ids":[device_id],"play":true})),
            Self::PlayTrack { uri } => {
                Request::new(Method::PUT, "me/player/play").body(json!({"uris":[uri]}))
            }
            Self::PlayPlaylist { uri } => {
                Request::new(Method::PUT, "me/player/play").body(json!({"context_uri":uri}))
            }
            Self::Queue { uri } => Request::new(Method::POST, "me/player/queue").param("uri", uri),
            Self::SaveTrack { id } => {
                Request::new(Method::PUT, "me/tracks").body(json!({"ids":[valid_id(id)?]}))
            }
            Self::RemoveSavedTrack { id } => {
                Request::new(Method::DELETE, "me/tracks").body(json!({"ids":[valid_id(id)?]}))
            }
        })
    }
}
impl SpotifyQuery {
    fn request(&self) -> ModuleResult<Request> {
        use reqwest::Method;
        Ok(match self {
            Self::Playback => Request::new(Method::GET, "me/player"),
            Self::Devices => Request::new(Method::GET, "me/player/devices"),
            Self::Queue => Request::new(Method::GET, "me/player/queue"),
            Self::Recent => {
                Request::new(Method::GET, "me/player/recently-played").param("limit", 50)
            }
            Self::Saved => Request::new(Method::GET, "me/tracks").param("limit", 50),
            Self::Playlists => Request::new(Method::GET, "me/playlists").param("limit", 50),
            Self::PlaylistTracks { id } => {
                Request::new(Method::GET, format!("playlists/{}/tracks", valid_id(id)?))
                    .param("limit", 50)
            }
            Self::Search { text } => Request::new(Method::GET, "search")
                .param("q", text)
                .param("type", "track")
                .param("limit", 50),
        })
    }
}
impl SpotifyApiClient {
    async fn perform(
        &self,
        token: &str,
        request: &Request,
        offset: Option<u64>,
    ) -> ModuleResult<Value> {
        let mut call = self
            .http
            .request(
                request.method.clone(),
                format!("{}{}", self.api_base, request.path),
            )
            .bearer_auth(token)
            .query(&request.params);
        if let Some(offset) = offset {
            call = call.query(&[("offset", offset)]);
        }
        if let Some(body) = &request.body {
            call = call.json(body);
        }
        let response = call.send().await?;
        let status = response.status();
        if status.as_u16() == 204 {
            return Ok(Value::Null);
        }
        let body = response.text().await?;
        if !status.is_success() {
            return Err(super::api::map_api_error(status.as_u16(), &body));
        }
        if body.is_empty() {
            Ok(Value::Null)
        } else {
            serde_json::from_str(&body).map_err(|e| ModuleError::Message(e.to_string()))
        }
    }
}
impl SpotifyClient {
    pub async fn action(&self, client_id: &str, action: SpotifyAction) -> ModuleResult<Value> {
        let request = action.request()?;
        let token = self.get_valid_token(client_id).await?;
        match self.api.perform(&token.access_token, &request, None).await {
            Err(e) if super::is_unauthorized(&e) => {
                let token = self.refresh_forced(client_id, &token.access_token).await?;
                self.api.perform(&token.access_token, &request, None).await
            }
            result => result,
        }
    }
    pub async fn query(
        &self,
        client_id: &str,
        query: SpotifyQuery,
        offset: Option<u64>,
    ) -> ModuleResult<Value> {
        let request = query.request()?;
        let token = self.get_valid_token(client_id).await?;
        match self
            .api
            .perform(&token.access_token, &request, offset)
            .await
        {
            Err(e) if super::is_unauthorized(&e) => {
                let token = self.refresh_forced(client_id, &token.access_token).await?;
                self.api
                    .perform(&token.access_token, &request, offset)
                    .await
            }
            result => result,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use wiremock::{
        matchers::{body_json, header, method, path, query_param},
        Mock, MockServer, ResponseTemplate,
    };
    #[tokio::test]
    async fn writes_send_bearer_and_correct_playback_payloads() {
        let server = MockServer::start().await;
        let api = SpotifyApiClient::with_base_url(server.uri());
        Mock::given(method("PUT"))
            .and(path("/me/player/volume"))
            .and(header("authorization", "Bearer token"))
            .and(query_param("volume_percent", "35"))
            .respond_with(ResponseTemplate::new(204))
            .expect(1)
            .mount(&server)
            .await;
        api.perform(
            "token",
            &SpotifyAction::Volume { percent: 35 }.request().unwrap(),
            None,
        )
        .await
        .unwrap();
        Mock::given(method("PUT"))
            .and(path("/me/player/play"))
            .and(body_json(json!({"context_uri":"spotify:playlist:abc"})))
            .respond_with(ResponseTemplate::new(204))
            .expect(1)
            .mount(&server)
            .await;
        api.perform(
            "token",
            &SpotifyAction::PlayPlaylist {
                uri: "spotify:playlist:abc".into(),
            }
            .request()
            .unwrap(),
            None,
        )
        .await
        .unwrap();
        assert!(SpotifyAction::Volume { percent: 101 }.request().is_err());
        assert!(SpotifyAction::Repeat {
            mode: "invalid".into()
        }
        .request()
        .is_err());
        assert!(SpotifyQuery::PlaylistTracks { id: "../me".into() }
            .request()
            .is_err());
    }
}
