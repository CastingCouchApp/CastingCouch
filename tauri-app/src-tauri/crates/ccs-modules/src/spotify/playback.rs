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
    pub async fn activate_preferred_device(
        &self,
        client_id: &str,
        options: &Value,
        play: bool,
    ) -> ModuleResult<Value> {
        let devices = self.query(client_id, SpotifyQuery::Devices, None).await?;
        let list = devices["devices"]
            .as_array()
            .ok_or_else(|| ModuleError::Message("Spotify-Geräteliste ist ungültig".into()))?;
        let preferred = options["PreferredDeviceId"].as_str().unwrap_or("").trim();
        let mut selected = list
            .iter()
            .find(|device| !preferred.is_empty() && device["id"].as_str() == Some(preferred));
        if selected.is_none()
            && options["UseActiveDeviceWhenPreferredUnavailable"]
                .as_bool()
                .unwrap_or(true)
        {
            let usable = |device: &&Value| {
                device["is_restricted"] != true
                    && device["id"].as_str().is_some_and(|id| !id.is_empty())
            };
            selected = list
                .iter()
                .filter(usable)
                .find(|device| device["is_active"] == true)
                .or_else(|| list.iter().find(usable));
        }
        let selected=selected.ok_or_else(||ModuleError::Message("Das gespeicherte Spotify-Standardgerät ist nicht erreichbar. Spotify dort öffnen und kurz einen Titel starten.".into()))?;
        if selected["is_restricted"] == true {
            return Err(ModuleError::Message(format!(
                "Das Spotify-Gerät '{}' ist eingeschränkt und kann nicht ferngesteuert werden.",
                selected["name"].as_str().unwrap_or("Unbekannt")
            )));
        }
        let id = selected["id"]
            .as_str()
            .filter(|id| !id.is_empty())
            .ok_or_else(|| ModuleError::Message("Spotify-Geräte-ID fehlt".into()))?;
        let playback = self.query(client_id, SpotifyQuery::Playback, None).await?;
        if selected["is_active"] != true || playback["device"]["id"].as_str() != Some(id) {
            self.perform_request(
                client_id,
                Request::new(reqwest::Method::PUT, "me/player")
                    .body(json!({"device_ids":[id],"play":play})),
            )
            .await?;
        }
        Ok(selected.clone())
    }

    pub async fn action_with_preferences(
        &self,
        client_id: &str,
        action: SpotifyAction,
        options: &Value,
    ) -> ModuleResult<Value> {
        let preferred = options["PreferredDeviceId"]
            .as_str()
            .filter(|id| !id.trim().is_empty());
        if matches!(
            &action,
            SpotifyAction::PlayPlaylist { .. } | SpotifyAction::PlayTrack { .. }
        ) && options["AutoTransferToPreferredDevice"]
            .as_bool()
            .unwrap_or(true)
        {
            let selected = self
                .activate_preferred_device(client_id, options, false)
                .await?;
            self.action_on_device(client_id, action, selected["id"].as_str())
                .await
        } else {
            self.action_on_device(client_id, action, preferred).await
        }
    }

    pub async fn action_on_device(
        &self,
        client_id: &str,
        action: SpotifyAction,
        device_id: Option<&str>,
    ) -> ModuleResult<Value> {
        let mut request = action.request()?;
        if matches!(
            &action,
            SpotifyAction::Play
                | SpotifyAction::Pause
                | SpotifyAction::Next
                | SpotifyAction::Previous
                | SpotifyAction::Volume { .. }
                | SpotifyAction::Seek { .. }
                | SpotifyAction::Shuffle { .. }
                | SpotifyAction::Repeat { .. }
                | SpotifyAction::PlayTrack { .. }
                | SpotifyAction::PlayPlaylist { .. }
                | SpotifyAction::Queue { .. }
        ) {
            if let Some(id) = device_id.filter(|id| !id.trim().is_empty()) {
                request = request.param("device_id", id.trim());
            }
        }
        self.perform_request(client_id, request).await
    }

    pub async fn set_device_volume(
        &self,
        client_id: &str,
        device_id: &str,
        percent: u8,
    ) -> ModuleResult<Value> {
        let request = SpotifyAction::Volume { percent }
            .request()?
            .param("device_id", device_id);
        self.perform_request(client_id, request).await
    }
    pub async fn action(&self, client_id: &str, action: SpotifyAction) -> ModuleResult<Value> {
        self.action_on_device(client_id, action, None).await
    }
    async fn perform_request(&self, client_id: &str, request: Request) -> ModuleResult<Value> {
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
