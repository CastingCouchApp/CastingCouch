mod api;
mod oauth;
mod tokens;

pub use api::{SpotifyApiClient, API_BASE_URL};
pub use oauth::{SpotifyOAuthClient, AUTHORIZE_URL, DEFAULT_REDIRECT_URI, TOKEN_URL};
pub use tokens::{NowPlaying, SpotifyTokenSet, SpotifyUser};

use crate::{ConnectionState, ModuleError, ModuleResult, ServiceStatus};
use ccs_secrets::{SecretStore, SPOTIFY_TOKEN_SET_KEY};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tokio::sync::{broadcast, Mutex, RwLock};
use tokio::task::JoinHandle;

use api::SpotifyApiClient as Api;
use oauth::SpotifyOAuthClient as OAuth;

pub struct SpotifyTokenRepository {
    secrets: Arc<dyn SecretStore>,
}

impl SpotifyTokenRepository {
    pub fn new(secrets: Arc<dyn SecretStore>) -> Self {
        Self { secrets }
    }

    pub fn save(&self, token: &SpotifyTokenSet) -> ModuleResult<()> {
        let json = serde_json::to_string(token)
            .map_err(|e| ModuleError::Message(format!("token serialize: {e}")))?;
        self.secrets
            .set(SPOTIFY_TOKEN_SET_KEY, &json)
            .map_err(|e| ModuleError::Message(e.to_string()))
    }

    pub fn load(&self) -> ModuleResult<Option<SpotifyTokenSet>> {
        let raw = self
            .secrets
            .get(SPOTIFY_TOKEN_SET_KEY)
            .map_err(|e| ModuleError::Message(e.to_string()))?;
        match raw {
            None => Ok(None),
            Some(json) => {
                let token = serde_json::from_str(&json)
                    .map_err(|e| ModuleError::Message(format!("token deserialize: {e}")))?;
                Ok(Some(token))
            }
        }
    }

    pub fn delete(&self) -> ModuleResult<()> {
        self.secrets
            .delete(SPOTIFY_TOKEN_SET_KEY)
            .map_err(|e| ModuleError::Message(e.to_string()))
    }
}

pub struct SpotifyConnectOptions {
    pub client_id: String,
    pub redirect_uri: String,
    pub scopes: Vec<String>,
}

pub struct SpotifyClient {
    status: RwLock<ServiceStatus>,
    tokens: SpotifyTokenRepository,
    oauth: OAuth,
    api: Api,
    now_playing: RwLock<NowPlaying>,
    display_name: RwLock<String>,
    login_task: Mutex<Option<JoinHandle<()>>>,
    poll_task: Mutex<Option<JoinHandle<()>>>,
    cancel_login: AtomicBool,
    poll_enabled: AtomicBool,
    status_tx: broadcast::Sender<ServiceStatus>,
    now_playing_tx: broadcast::Sender<NowPlaying>,
}

impl SpotifyClient {
    fn channels() -> (
        broadcast::Sender<ServiceStatus>,
        broadcast::Sender<NowPlaying>,
    ) {
        let (status_tx, _) = broadcast::channel(16);
        let (now_playing_tx, _) = broadcast::channel(16);
        (status_tx, now_playing_tx)
    }

    pub fn new(secrets: Arc<dyn SecretStore>) -> Self {
        let (status_tx, now_playing_tx) = Self::channels();
        Self {
            status: RwLock::new(ServiceStatus::disconnected("spotify", "Spotify")),
            tokens: SpotifyTokenRepository::new(secrets),
            oauth: OAuth::new(),
            api: Api::new(),
            now_playing: RwLock::new(NowPlaying::default()),
            display_name: RwLock::new(String::new()),
            login_task: Mutex::new(None),
            poll_task: Mutex::new(None),
            cancel_login: AtomicBool::new(false),
            poll_enabled: AtomicBool::new(false),
            status_tx,
            now_playing_tx,
        }
    }

    pub fn new_shared(secrets: Arc<dyn SecretStore>) -> Arc<Self> {
        Arc::new(Self::new(secrets))
    }

    pub fn with_http(
        secrets: Arc<dyn SecretStore>,
        oauth: OAuth,
        api_base: impl Into<String>,
    ) -> Self {
        let (status_tx, now_playing_tx) = Self::channels();
        Self {
            status: RwLock::new(ServiceStatus::disconnected("spotify", "Spotify")),
            tokens: SpotifyTokenRepository::new(secrets),
            oauth,
            api: Api::with_base_url(api_base),
            now_playing: RwLock::new(NowPlaying::default()),
            display_name: RwLock::new(String::new()),
            login_task: Mutex::new(None),
            poll_task: Mutex::new(None),
            cancel_login: AtomicBool::new(false),
            poll_enabled: AtomicBool::new(false),
            status_tx,
            now_playing_tx,
        }
    }

    pub fn token_url() -> &'static str {
        TOKEN_URL
    }

    pub fn currently_playing_url() -> &'static str {
        "https://api.spotify.com/v1/me/player/currently-playing"
    }

    pub async fn status(&self) -> ServiceStatus {
        self.status.read().await.clone()
    }

    pub async fn now_playing(&self) -> NowPlaying {
        self.now_playing.read().await.clone()
    }

    pub fn subscribe_status(&self) -> broadcast::Receiver<ServiceStatus> {
        self.status_tx.subscribe()
    }

    pub fn subscribe_now_playing(&self) -> broadcast::Receiver<NowPlaying> {
        self.now_playing_tx.subscribe()
    }

    pub fn has_token(&self) -> bool {
        matches!(self.tokens.load(), Ok(Some(_)))
    }

    async fn set_status(&self, state: ConnectionState, detail: impl Into<String>) {
        let snapshot = {
            let mut s = self.status.write().await;
            s.state = state;
            s.detail = detail.into();
            s.clone()
        };
        let _ = self.status_tx.send(snapshot);
    }

    async fn store_now_playing(&self, playing: NowPlaying) {
        let changed = {
            let mut np = self.now_playing.write().await;
            let changed = *np != playing;
            *np = playing.clone();
            changed
        };
        if changed {
            let _ = self.now_playing_tx.send(playing);
        }
    }

    pub async fn begin_login(
        self: &Arc<Self>,
        options: SpotifyConnectOptions,
    ) -> ModuleResult<(ServiceStatus, String)> {
        OAuth::validate_client_id(&options.client_id)?;
        self.cancel_pending_login().await;

        let pending = self
            .oauth
            .start_authorization(&options.client_id, &options.redirect_uri, &options.scopes)
            .await?;
        let authorize_url = pending.authorize_url.clone();
        let verifier = pending.verifier.clone();
        let redirect_uri = pending.redirect_uri.clone();

        self.cancel_login.store(false, Ordering::SeqCst);
        self.set_status(ConnectionState::Connecting, "Warte auf Spotify-Anmeldung …")
            .await;

        let this = Arc::clone(self);
        let client_id = options.client_id.clone();
        let scopes = options.scopes.clone();
        let handle = tokio::spawn(async move {
            match OAuth::wait_for_code(pending, &this.cancel_login).await {
                Ok(code) => {
                    if this.cancel_login.load(Ordering::SeqCst) {
                        return;
                    }
                    match this
                        .oauth
                        .exchange_code(&client_id, &redirect_uri, &code, &verifier)
                        .await
                    {
                        Ok(token) => {
                            if let Err(e) = this.tokens.save(&token) {
                                this.set_status(ConnectionState::Error, e.to_string()).await;
                                return;
                            }
                            let poll_id = client_id.clone();
                            let connect_opts = SpotifyConnectOptions {
                                client_id,
                                redirect_uri,
                                scopes,
                            };
                            if let Err(e) = this.connect(&connect_opts).await {
                                this.set_status(ConnectionState::Error, e.to_string()).await;
                            } else {
                                this.spawn_poll(poll_id);
                            }
                        }
                        Err(e) => {
                            this.set_status(ConnectionState::Error, e.to_string()).await;
                        }
                    }
                }
                Err(e) => {
                    if !this.cancel_login.load(Ordering::SeqCst) {
                        this.set_status(ConnectionState::Error, e.to_string()).await;
                    }
                }
            }
        });
        *self.login_task.lock().await = Some(handle);
        Ok((self.status().await, authorize_url))
    }

    pub async fn connect(&self, options: &SpotifyConnectOptions) -> ModuleResult<ServiceStatus> {
        OAuth::validate_client_id(&options.client_id)?;
        self.set_status(ConnectionState::Connecting, "Verbinde …")
            .await;

        let token = match self.get_valid_token(&options.client_id).await {
            Ok(token) => token,
            Err(e) => {
                self.set_status(ConnectionState::Error, e.to_string()).await;
                return Err(e);
            }
        };
        let user = match self.api.get_current_user(&token.access_token).await {
            Ok(user) => user,
            Err(e) if is_unauthorized(&e) => {
                let token = self.refresh_forced(&options.client_id).await?;
                self.api.get_current_user(&token.access_token).await?
            }
            Err(e) => {
                self.set_status(ConnectionState::Error, e.to_string()).await;
                return Err(e);
            }
        };
        *self.display_name.write().await = user.display_name.clone();

        let playing = match self.fetch_now_playing(&options.client_id).await {
            Ok(p) => p,
            Err(e) => {
                // Token is valid; surface playback errors in detail but stay connected.
                self.set_status(
                    ConnectionState::Connected,
                    format!("{} · {e}", user.display_name),
                )
                .await;
                return Ok(self.status().await);
            }
        };
        self.store_now_playing(playing.clone()).await;
        self.set_status(
            ConnectionState::Connected,
            status_detail(&user.display_name, &playing),
        )
        .await;
        Ok(self.status().await)
    }

    pub async fn logout(&self) -> ModuleResult<ServiceStatus> {
        self.cancel_pending_login().await;
        self.stop_poll().await;
        self.tokens.delete()?;
        self.store_now_playing(NowPlaying::default()).await;
        *self.display_name.write().await = String::new();
        self.set_status(ConnectionState::Disconnected, "").await;
        Ok(self.status().await)
    }

    pub async fn refresh_now_playing(&self, client_id: &str) -> ModuleResult<NowPlaying> {
        if !self.has_token() {
            return Ok(NowPlaying::default());
        }
        match self.fetch_now_playing(client_id).await {
            Ok(playing) => {
                self.store_now_playing(playing.clone()).await;
                let display = self.display_name.read().await.clone();
                if self.status().await.state == ConnectionState::Connected
                    || self.status().await.state == ConnectionState::Error
                {
                    self.set_status(
                        ConnectionState::Connected,
                        status_detail(&display, &playing),
                    )
                    .await;
                }
                Ok(playing)
            }
            Err(e) => {
                self.set_status(ConnectionState::Error, e.to_string()).await;
                Err(e)
            }
        }
    }

    pub async fn set_now_playing(&self, track: NowPlaying) {
        self.store_now_playing(track).await;
        let mut s = self.status.write().await;
        s.state = ConnectionState::Connected;
    }

    async fn fetch_now_playing(&self, client_id: &str) -> ModuleResult<NowPlaying> {
        let token = self.get_valid_token(client_id).await?;
        match self.api.get_currently_playing(&token.access_token).await {
            Ok(playing) => Ok(playing),
            Err(e) if is_unauthorized(&e) => {
                let token = self.refresh_forced(client_id).await?;
                self.api.get_currently_playing(&token.access_token).await
            }
            Err(e) => Err(e),
        }
    }

    async fn get_valid_token(&self, client_id: &str) -> ModuleResult<SpotifyTokenSet> {
        let token = self
            .tokens
            .load()?
            .ok_or_else(|| ModuleError::Message("Spotify wurde noch nicht autorisiert.".into()))?;
        if !token.is_expired() {
            return Ok(token);
        }
        self.refresh_forced(client_id).await
    }

    async fn refresh_forced(&self, client_id: &str) -> ModuleResult<SpotifyTokenSet> {
        let old = self
            .tokens
            .load()?
            .ok_or_else(|| ModuleError::Message("Spotify wurde noch nicht autorisiert.".into()))?;
        if old.refresh_token.trim().is_empty() {
            return Err(ModuleError::Message(
                "Der Spotify-Token ist abgelaufen. Bitte Spotify neu autorisieren.".into(),
            ));
        }
        let mut refreshed = self.oauth.refresh(client_id, &old.refresh_token).await?;
        if refreshed.refresh_token.trim().is_empty() {
            refreshed.refresh_token = old.refresh_token.clone();
        }
        if refreshed.scopes.is_empty() && !old.scopes.is_empty() {
            refreshed.scopes = old.scopes.clone();
        }
        self.tokens.save(&refreshed)?;
        Ok(refreshed)
    }

    pub fn spawn_poll(self: &Arc<Self>, client_id: String) {
        let this = Arc::clone(self);
        this.poll_enabled.store(true, Ordering::SeqCst);
        tokio::spawn(async move {
            if let Some(prev) = this.poll_task.lock().await.take() {
                prev.abort();
            }
            let poller = Arc::clone(&this);
            let handle = tokio::spawn(async move {
                loop {
                    tokio::time::sleep(std::time::Duration::from_secs(10)).await;
                    if !poller.poll_enabled.load(Ordering::SeqCst) {
                        break;
                    }
                    let _ = poller.refresh_now_playing(&client_id).await;
                }
            });
            *this.poll_task.lock().await = Some(handle);
        });
    }

    async fn stop_poll(&self) {
        self.poll_enabled.store(false, Ordering::SeqCst);
        if let Some(handle) = self.poll_task.lock().await.take() {
            handle.abort();
        }
    }

    async fn cancel_pending_login(&self) {
        self.cancel_login.store(true, Ordering::SeqCst);
        if let Some(handle) = self.login_task.lock().await.take() {
            handle.abort();
        }
    }
}

fn status_detail(display_name: &str, playing: &NowPlaying) -> String {
    if !playing.title.is_empty() {
        let track = if playing.artist.is_empty() {
            playing.title.clone()
        } else {
            format!("{} – {}", playing.title, playing.artist)
        };
        if display_name.is_empty() {
            track
        } else {
            format!("{display_name} · {track}")
        }
    } else if !display_name.is_empty() {
        display_name.to_string()
    } else {
        "Keine Wiedergabe".into()
    }
}

fn is_unauthorized(err: &ModuleError) -> bool {
    let text = err.to_string();
    text.contains("401") || text.to_ascii_lowercase().contains("unauthorized")
}

#[cfg(test)]
mod tests {
    use super::*;
    use ccs_secrets::MemorySecretStore;
    use serde_json::json;
    use std::sync::atomic::{AtomicUsize, Ordering as AtomicOrdering};
    use wiremock::matchers::{body_string_contains, header, method, path};
    use wiremock::{Mock, MockServer, ResponseTemplate};

    fn fixture(name: &str) -> String {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("src/spotify/fixtures")
            .join(name);
        std::fs::read_to_string(path).expect("fixture")
    }

    fn contract_client_id() -> String {
        "contract-client-id-12345".into()
    }

    async fn free_redirect_uri() -> String {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0")
            .await
            .expect("bind");
        let port = listener.local_addr().unwrap().port();
        drop(listener);
        format!("http://127.0.0.1:{port}/callback/")
    }

    #[test]
    fn pkce_challenge_is_deterministic_and_url_safe() {
        const VERIFIER: &str = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
        let first = OAuth::create_code_challenge(VERIFIER);
        let second = OAuth::create_code_challenge(VERIFIER);
        assert_eq!(first, second);
        assert!(!first.is_empty());
        assert!(!first.contains('+'));
        assert!(!first.contains('/'));
        assert!(!first.contains('='));
    }

    #[test]
    fn token_repository_roundtrip_and_delete() {
        let store = Arc::new(MemorySecretStore::new());
        let repo = SpotifyTokenRepository::new(store.clone());
        let token = SpotifyTokenSet::from_oauth(
            "access".into(),
            "refresh".into(),
            3600,
            "Bearer".into(),
            vec!["user-read-currently-playing".into()],
        );
        repo.save(&token).unwrap();
        let loaded = repo.load().unwrap().unwrap();
        assert_eq!(loaded.access_token, "access");
        assert_eq!(loaded.refresh_token, "refresh");
        assert_eq!(loaded.token_type, "Bearer");
        let raw = store.get(SPOTIFY_TOKEN_SET_KEY).unwrap().unwrap();
        assert!(raw.contains("\"AccessToken\""));
        assert!(raw.contains("\"RefreshToken\""));
        assert!(raw.contains("\"ExpiresInSeconds\""));
        assert!(raw.contains("\"TokenType\""));
        assert!(raw.contains("\"Scopes\""));
        assert!(raw.contains("\"ObtainedAt\""));
        repo.delete().unwrap();
        assert!(repo.load().unwrap().is_none());
    }

    #[tokio::test]
    async fn currently_playing_maps_track_and_requires_bearer() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/me/player/currently-playing"))
            .and(header("Authorization", "Bearer contract-token"))
            .respond_with(
                ResponseTemplate::new(200).set_body_string(fixture("currently-playing-track.json")),
            )
            .mount(&server)
            .await;

        let api = Api::with_base_url(format!("{}/", server.uri()));
        let playing = api.get_currently_playing("contract-token").await.unwrap();
        assert_eq!(playing.title, "Contract Song");
        assert_eq!(playing.artist, "Contract Artist, Guest Artist");
        assert_eq!(playing.album, "Contract Album");
        assert!(playing.is_playing);
    }

    #[tokio::test]
    async fn currently_playing_null_item_is_empty() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/me/player/currently-playing"))
            .respond_with(
                ResponseTemplate::new(200)
                    .set_body_string(fixture("currently-playing-null-item.json")),
            )
            .mount(&server)
            .await;

        let api = Api::with_base_url(format!("{}/", server.uri()));
        let playing = api.get_currently_playing("contract-token").await.unwrap();
        assert!(playing.title.is_empty());
        assert!(playing.artist.is_empty());
        assert!(playing.album.is_empty());
        assert!(!playing.is_playing);
    }

    #[tokio::test]
    async fn currently_playing_204_is_empty_and_connect_stays_connected() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/me/player/currently-playing"))
            .respond_with(ResponseTemplate::new(204))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/me"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("me.json")))
            .mount(&server)
            .await;

        let store = Arc::new(MemorySecretStore::new());
        let oauth = OAuth::with_base_urls(
            format!("{}/authorize", server.uri()),
            format!("{}/api/token", server.uri()),
        );
        let client = SpotifyClient::with_http(store.clone(), oauth, format!("{}/", server.uri()));
        let token = SpotifyTokenSet::from_oauth(
            "contract-access".into(),
            "contract-refresh".into(),
            3600,
            "Bearer".into(),
            vec!["user-read-currently-playing".into()],
        );
        client.tokens.save(&token).unwrap();
        let status = client
            .connect(&SpotifyConnectOptions {
                client_id: contract_client_id(),
                redirect_uri: DEFAULT_REDIRECT_URI.into(),
                scopes: vec!["user-read-currently-playing".into()],
            })
            .await
            .unwrap();
        assert_eq!(status.state, ConnectionState::Connected);
        assert!(status.detail.contains("Contract User"));
        let playing = client.now_playing().await;
        assert!(playing.title.is_empty());
        assert!(!playing.is_playing);
    }

    #[tokio::test]
    async fn oauth_exchange_posts_pkce_without_client_secret() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/token"))
            .and(body_string_contains("grant_type=authorization_code"))
            .and(body_string_contains("code_verifier=test-verifier"))
            .and(body_string_contains("client_id=contract-client-id-12345"))
            .respond_with(move |req: &wiremock::Request| {
                let body = String::from_utf8_lossy(&req.body);
                assert!(
                    !body.contains("client_secret"),
                    "PKCE token request must not send a client_secret"
                );
                ResponseTemplate::new(200).set_body_string(fixture("token.json"))
            })
            .mount(&server)
            .await;

        let oauth = OAuth::with_base_urls(
            format!("{}/authorize", server.uri()),
            format!("{}/api/token", server.uri()),
        );
        let token = oauth
            .exchange_code(
                &contract_client_id(),
                DEFAULT_REDIRECT_URI,
                "auth-code",
                "test-verifier",
            )
            .await
            .unwrap();
        assert_eq!(token.access_token, "contract-access");
        assert_eq!(token.refresh_token, "contract-refresh");
        assert!(token
            .scopes
            .contains(&"user-read-currently-playing".to_string()));
    }

    #[tokio::test]
    async fn oauth_error_does_not_include_tokens() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/token"))
            .respond_with(ResponseTemplate::new(400).set_body_json(json!({
                "error": "invalid_grant",
                "error_description": "Invalid authorization code",
                "access_token": "secret-should-not-leak",
                "refresh_token": "refresh-should-not-leak"
            })))
            .mount(&server)
            .await;

        let oauth = OAuth::with_base_urls(
            format!("{}/authorize", server.uri()),
            format!("{}/api/token", server.uri()),
        );
        let err = oauth
            .exchange_code(&contract_client_id(), DEFAULT_REDIRECT_URI, "bad", "v")
            .await
            .unwrap_err()
            .to_string();
        assert!(err.contains("Invalid authorization code"));
        assert!(!err.contains("secret-should-not-leak"));
        assert!(!err.contains("refresh-should-not-leak"));
        assert!(!err.contains("access_token"));
        assert!(!err.contains("refresh_token"));
    }

    #[tokio::test]
    async fn refresh_keeps_old_refresh_token_and_scopes_when_omitted() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/token"))
            .and(body_string_contains("grant_type=refresh_token"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({
                "access_token": "new-access",
                "expires_in": 3600,
                "token_type": "Bearer"
            })))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/me"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("me.json")))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/me/player/currently-playing"))
            .respond_with(ResponseTemplate::new(204))
            .mount(&server)
            .await;

        let store = Arc::new(MemorySecretStore::new());
        let oauth = OAuth::with_base_urls(
            format!("{}/authorize", server.uri()),
            format!("{}/api/token", server.uri()),
        );
        let client = SpotifyClient::with_http(store.clone(), oauth, format!("{}/", server.uri()));
        let mut old = SpotifyTokenSet::from_oauth(
            "old-access".into(),
            "old-refresh".into(),
            60,
            "Bearer".into(),
            vec!["user-read-currently-playing".into()],
        );
        old.obtained_at = chrono::Utc::now() - chrono::Duration::seconds(120);
        client.tokens.save(&old).unwrap();

        client
            .connect(&SpotifyConnectOptions {
                client_id: contract_client_id(),
                redirect_uri: DEFAULT_REDIRECT_URI.into(),
                scopes: vec!["user-read-currently-playing".into()],
            })
            .await
            .unwrap();
        let saved = client.tokens.load().unwrap().unwrap();
        assert_eq!(saved.access_token, "new-access");
        assert_eq!(saved.refresh_token, "old-refresh");
        assert_eq!(
            saved.scopes,
            vec!["user-read-currently-playing".to_string()]
        );
    }

    #[tokio::test]
    async fn login_happy_path_stores_token_and_now_playing() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/token"))
            .and(body_string_contains("grant_type=authorization_code"))
            .and(body_string_contains("code_verifier="))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("token.json")))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/me"))
            .and(header("Authorization", "Bearer contract-access"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("me.json")))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/me/player/currently-playing"))
            .and(header("Authorization", "Bearer contract-access"))
            .respond_with(
                ResponseTemplate::new(200).set_body_string(fixture("currently-playing-track.json")),
            )
            .mount(&server)
            .await;

        let store = Arc::new(MemorySecretStore::new());
        let oauth = OAuth::with_base_urls(
            format!("{}/authorize", server.uri()),
            format!("{}/api/token", server.uri()),
        );
        let client = Arc::new(SpotifyClient::with_http(
            store.clone(),
            oauth,
            format!("{}/", server.uri()),
        ));
        let redirect = free_redirect_uri().await;
        let (status, uri) = client
            .begin_login(SpotifyConnectOptions {
                client_id: contract_client_id(),
                redirect_uri: redirect.clone(),
                scopes: vec!["user-read-currently-playing".into()],
            })
            .await
            .unwrap();
        assert_eq!(status.state, ConnectionState::Connecting);
        assert!(uri.contains("code_challenge_method=S256"));
        assert!(uri.contains("show_dialog=true"));
        let parsed = url::Url::parse(&uri).unwrap();
        let state = parsed
            .query_pairs()
            .find(|(k, _)| k == "state")
            .unwrap()
            .1
            .into_owned();

        let callback = format!(
            "{}?code=auth-code&state={state}",
            redirect.trim_end_matches('/')
        );
        let http = reqwest::Client::new();
        let _ = http.get(&callback).send().await;

        for _ in 0..40 {
            tokio::time::sleep(std::time::Duration::from_millis(100)).await;
            if client.status().await.state == ConnectionState::Connected {
                break;
            }
        }
        let final_status = client.status().await;
        assert_eq!(final_status.state, ConnectionState::Connected);
        assert!(final_status.detail.contains("Contract Song"));
        assert!(store.get(SPOTIFY_TOKEN_SET_KEY).unwrap().is_some());
        let playing = client.now_playing().await;
        assert_eq!(playing.title, "Contract Song");
        assert!(playing.is_playing);

        let after = client.logout().await.unwrap();
        assert_eq!(after.state, ConnectionState::Disconnected);
        assert!(store.get(SPOTIFY_TOKEN_SET_KEY).unwrap().is_none());
        assert!(client.now_playing().await.title.is_empty());
    }

    #[tokio::test]
    async fn connect_unauthorized_after_failed_refresh_sets_error_detail() {
        let server = MockServer::start().await;
        let calls = Arc::new(AtomicUsize::new(0));
        let calls_clone = Arc::clone(&calls);
        Mock::given(method("POST"))
            .and(path("/api/token"))
            .respond_with(move |_req: &wiremock::Request| {
                calls_clone.fetch_add(1, AtomicOrdering::SeqCst);
                ResponseTemplate::new(401).set_body_json(json!({
                    "error": "invalid_grant",
                    "error_description": "Refresh token revoked"
                }))
            })
            .mount(&server)
            .await;

        let store = Arc::new(MemorySecretStore::new());
        let oauth = OAuth::with_base_urls(
            format!("{}/authorize", server.uri()),
            format!("{}/api/token", server.uri()),
        );
        let client = SpotifyClient::with_http(store.clone(), oauth, format!("{}/", server.uri()));
        let mut old = SpotifyTokenSet::from_oauth(
            "old-access".into(),
            "old-refresh".into(),
            60,
            "Bearer".into(),
            vec!["user-read-currently-playing".into()],
        );
        old.obtained_at = chrono::Utc::now() - chrono::Duration::seconds(120);
        client.tokens.save(&old).unwrap();

        let err = client
            .connect(&SpotifyConnectOptions {
                client_id: contract_client_id(),
                redirect_uri: DEFAULT_REDIRECT_URI.into(),
                scopes: vec!["user-read-currently-playing".into()],
            })
            .await
            .unwrap_err()
            .to_string();
        assert!(err.contains("Refresh token revoked"));
        assert!(!err.contains("old-access"));
        assert!(!err.contains("old-refresh"));
        assert_eq!(client.status().await.state, ConnectionState::Error);
        assert!(client
            .status()
            .await
            .detail
            .contains("Refresh token revoked"));
    }

    #[tokio::test]
    async fn refresh_now_playing_broadcasts_track_change() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/me/player/currently-playing"))
            .and(header("Authorization", "Bearer contract-access"))
            .respond_with(
                ResponseTemplate::new(200).set_body_string(fixture("currently-playing-track.json")),
            )
            .mount(&server)
            .await;

        let store = Arc::new(MemorySecretStore::new());
        let oauth = OAuth::with_base_urls(
            format!("{}/authorize", server.uri()),
            format!("{}/api/token", server.uri()),
        );
        let client = SpotifyClient::with_http(store.clone(), oauth, format!("{}/", server.uri()));
        let token = SpotifyTokenSet::from_oauth(
            "contract-access".into(),
            "contract-refresh".into(),
            3600,
            "Bearer".into(),
            vec!["user-read-currently-playing".into()],
        );
        client.tokens.save(&token).unwrap();

        let mut tracks = client.subscribe_now_playing();
        let playing = client
            .refresh_now_playing(&contract_client_id())
            .await
            .unwrap();
        assert_eq!(playing.title, "Contract Song");
        assert_eq!(playing.artist, "Contract Artist, Guest Artist");

        let got = tracks.recv().await.expect("now-playing broadcast");
        assert_eq!(got.title, "Contract Song");
        assert!(got.is_playing);
    }

    #[test]
    fn rejects_placeholder_client_id() {
        let err = OAuth::validate_client_id("your_client_id_placeholder").unwrap_err();
        assert!(err.to_string().contains("ungültig"));
    }

    #[test]
    fn currently_playing_url_contract() {
        assert_eq!(
            SpotifyClient::currently_playing_url(),
            "https://api.spotify.com/v1/me/player/currently-playing"
        );
        assert_eq!(
            SpotifyClient::token_url(),
            "https://accounts.spotify.com/api/token"
        );
    }
}
