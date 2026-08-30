mod eventsub;
mod helix;
mod oauth;
mod tokens;

pub use eventsub::{alert_type_for_event, EventSubClient, TwitchEvent};
pub use helix::{TwitchHelixClient, HELIX_BASE_URL};
pub use oauth::{TwitchOAuthClient, OAUTH_DEVICE_URL, OAUTH_TOKEN_URL, OAUTH_VALIDATE_URL};
pub use tokens::{TwitchDeviceCode, TwitchHelixUser, TwitchTokenSet, TwitchTokenValidation};

use crate::{ConnectionState, ModuleError, ModuleResult, ServiceStatus};
use ccs_secrets::{SecretStore, TWITCH_TOKEN_SET_KEY};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tokio::sync::{broadcast, Mutex, RwLock};
use tokio::task::JoinHandle;

pub fn eventsub_url() -> &'static str {
    "wss://eventsub.wss.twitch.tv/ws"
}

pub fn helix_users_url(login: &str) -> String {
    TwitchHelixClient::helix_users_url(Some(login))
}

/// Persists TwitchTokenSet JSON under `twitch.tokenSet`.
pub struct TwitchTokenRepository {
    secrets: Arc<dyn SecretStore>,
}

impl TwitchTokenRepository {
    pub fn new(secrets: Arc<dyn SecretStore>) -> Self {
        Self { secrets }
    }

    pub fn save(&self, token: &TwitchTokenSet) -> ModuleResult<()> {
        let json = serde_json::to_string(token)
            .map_err(|e| ModuleError::Message(format!("token serialize: {e}")))?;
        self.secrets
            .set(TWITCH_TOKEN_SET_KEY, &json)
            .map_err(|e| ModuleError::Message(e.to_string()))
    }

    pub fn load(&self) -> ModuleResult<Option<TwitchTokenSet>> {
        let raw = self
            .secrets
            .get(TWITCH_TOKEN_SET_KEY)
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
            .delete(TWITCH_TOKEN_SET_KEY)
            .map_err(|e| ModuleError::Message(e.to_string()))
    }
}

pub struct TwitchConnectOptions {
    pub client_id: String,
    pub channel_name: String,
    pub scopes: Vec<String>,
    pub enable_event_sub: bool,
}

/// Twitch Device-Code OAuth + Helix user status + EventSub.
pub struct TwitchClient {
    status: RwLock<ServiceStatus>,
    tokens: TwitchTokenRepository,
    oauth: TwitchOAuthClient,
    helix_base: String,
    eventsub_url: Option<String>,
    eventsub: Arc<EventSubClient>,
    events_tx: broadcast::Sender<TwitchEvent>,
    status_tx: broadcast::Sender<ServiceStatus>,
    current_user: RwLock<Option<TwitchHelixUser>>,
    login_task: Mutex<Option<JoinHandle<()>>>,
    cancel_login: AtomicBool,
}

impl TwitchClient {
    fn inner(
        secrets: Arc<dyn SecretStore>,
        oauth: TwitchOAuthClient,
        helix_base: String,
        eventsub_url: Option<String>,
    ) -> Self {
        let (events_tx, _) = broadcast::channel(64);
        let (status_tx, _) = broadcast::channel(16);
        Self {
            status: RwLock::new(ServiceStatus::disconnected("twitch", "Twitch")),
            tokens: TwitchTokenRepository::new(secrets),
            oauth,
            helix_base,
            eventsub_url,
            eventsub: EventSubClient::new_shared(),
            events_tx,
            status_tx,
            current_user: RwLock::new(None),
            login_task: Mutex::new(None),
            cancel_login: AtomicBool::new(false),
        }
    }

    pub fn new(secrets: Arc<dyn SecretStore>) -> Self {
        Self::inner(
            secrets,
            TwitchOAuthClient::new(),
            HELIX_BASE_URL.into(),
            Some(eventsub_url().into()),
        )
    }

    pub fn new_shared(secrets: Arc<dyn SecretStore>) -> Arc<Self> {
        Arc::new(Self::new(secrets))
    }

    pub fn with_http(
        secrets: Arc<dyn SecretStore>,
        oauth: TwitchOAuthClient,
        helix_base: impl Into<String>,
    ) -> Self {
        Self::inner(secrets, oauth, helix_base.into(), None)
    }

    pub fn with_http_and_eventsub(
        secrets: Arc<dyn SecretStore>,
        oauth: TwitchOAuthClient,
        helix_base: impl Into<String>,
        eventsub_url: impl Into<String>,
    ) -> Self {
        Self::inner(secrets, oauth, helix_base.into(), Some(eventsub_url.into()))
    }

    pub async fn status(&self) -> ServiceStatus {
        self.status.read().await.clone()
    }

    pub fn subscribe_status(&self) -> broadcast::Receiver<ServiceStatus> {
        self.status_tx.subscribe()
    }

    pub fn subscribe_events(&self) -> broadcast::Receiver<TwitchEvent> {
        self.events_tx.subscribe()
    }

    pub async fn current_user(&self) -> Option<TwitchHelixUser> {
        self.current_user.read().await.clone()
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

    /// Start device-code login. Returns immediately with connecting + user_code.
    /// Caller should open `verification_uri` (e.g. via opener plugin).
    pub async fn begin_login(
        self: &Arc<Self>,
        options: TwitchConnectOptions,
    ) -> ModuleResult<(ServiceStatus, String)> {
        TwitchOAuthClient::validate_client_id(&options.client_id)?;
        self.cancel_pending_login().await;

        let device = self
            .oauth
            .start_device_authorization(&options.client_id, &options.scopes)
            .await?;

        self.cancel_login.store(false, Ordering::SeqCst);
        self.set_status(
            ConnectionState::Connecting,
            format!("Code: {}", device.user_code),
        )
        .await;

        let verification_uri = device.verification_uri.clone();
        let this = Arc::clone(self);
        let connect_opts = TwitchConnectOptions {
            client_id: options.client_id.clone(),
            channel_name: options.channel_name.clone(),
            scopes: options.scopes.clone(),
            enable_event_sub: options.enable_event_sub,
        };
        let handle = tokio::spawn(async move {
            match this
                .oauth
                .wait_for_device_authorization(&connect_opts.client_id, &device)
                .await
            {
                Ok(token) => {
                    if this.cancel_login.load(Ordering::SeqCst) {
                        return;
                    }
                    if let Err(e) = this.tokens.save(&token) {
                        this.set_status(ConnectionState::Error, e.to_string()).await;
                        return;
                    }
                    if let Err(e) = this.connect(&connect_opts).await {
                        this.set_status(ConnectionState::Error, e.to_string()).await;
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

        Ok((self.status().await, verification_uri))
    }

    /// Validate token + Helix /users. Sets connected with display name.
    pub async fn connect(&self, options: &TwitchConnectOptions) -> ModuleResult<ServiceStatus> {
        TwitchOAuthClient::validate_client_id(&options.client_id)?;
        self.eventsub.stop().await;
        self.set_status(ConnectionState::Connecting, "Verbinde …")
            .await;

        let token = self.get_valid_token(&options.client_id).await?;
        let validation = self.oauth.validate(&token.access_token).await?;

        let helix = TwitchHelixClient::with_base_url(
            &self.helix_base,
            &options.client_id,
            &token.access_token,
        );
        let user = helix.get_current_user().await?;

        let (display, broadcaster_id) = if options.channel_name.trim().is_empty() {
            (user.display_name.clone(), user.id.clone())
        } else {
            match helix.get_user_by_login(&options.channel_name).await? {
                Some(channel) => (channel.display_name, channel.id),
                None => {
                    let msg = "Der konfigurierte Twitch-Kanal wurde nicht gefunden.";
                    self.set_status(ConnectionState::Error, msg).await;
                    return Err(ModuleError::Message(msg.into()));
                }
            }
        };

        if options.enable_event_sub {
            if let Some(url) = &self.eventsub_url {
                if let Err(e) = self
                    .eventsub
                    .connect(
                        url,
                        helix.clone(),
                        &broadcaster_id,
                        &user.id,
                        self.events_tx.clone(),
                    )
                    .await
                {
                    self.set_status(ConnectionState::Error, e.to_string()).await;
                    return Err(e);
                }
            }
        }

        *self.current_user.write().await = Some(user);
        let detail = format!("{display} ({})", validation.login);
        self.set_status(ConnectionState::Connected, detail).await;
        Ok(self.status().await)
    }

    /// Delete token from secret store and clear connection state.
    pub async fn logout(&self) -> ModuleResult<ServiceStatus> {
        self.cancel_pending_login().await;
        self.eventsub.stop().await;
        self.tokens.delete()?;
        *self.current_user.write().await = None;
        self.set_status(ConnectionState::Disconnected, "").await;
        Ok(self.status().await)
    }

    async fn cancel_pending_login(&self) {
        self.cancel_login.store(true, Ordering::SeqCst);
        if let Some(handle) = self.login_task.lock().await.take() {
            handle.abort();
        }
    }

    async fn get_valid_token(&self, client_id: &str) -> ModuleResult<TwitchTokenSet> {
        let mut token = self
            .tokens
            .load()?
            .ok_or_else(|| ModuleError::Message("Twitch ist noch nicht autorisiert.".into()))?;

        match self.oauth.validate(&token.access_token).await {
            Ok(v) if v.expires_in_seconds > 300 => return Ok(token),
            Ok(_) | Err(_) => {}
        }

        if token.refresh_token.trim().is_empty() {
            return Err(ModuleError::Message(
                "Twitch-Token abgelaufen. Bitte neu autorisieren.".into(),
            ));
        }

        token = self.oauth.refresh(client_id, &token.refresh_token).await?;
        self.tokens.save(&token)?;
        Ok(token)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use ccs_secrets::MemorySecretStore;
    use serde_json::json;
    use std::sync::atomic::{AtomicUsize, Ordering as AtomicOrdering};
    use wiremock::matchers::{header, method, path, path_regex};
    use wiremock::{Mock, MockServer, ResponseTemplate};

    fn fixture(name: &str) -> String {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("src/twitch/fixtures")
            .join(name);
        std::fs::read_to_string(path).expect("fixture")
    }

    fn contract_client_id() -> String {
        "contract-client-id-12345".into()
    }

    #[test]
    fn token_repository_roundtrip_and_delete() {
        let store = Arc::new(MemorySecretStore::new());
        let repo = TwitchTokenRepository::new(Arc::clone(&store) as Arc<dyn SecretStore>);
        let token = TwitchTokenSet::from_oauth(
            "access".into(),
            "refresh".into(),
            3600,
            vec!["user:read:chat".into()],
        );
        repo.save(&token).unwrap();
        let loaded = repo.load().unwrap().unwrap();
        assert_eq!(loaded.access_token, "access");
        assert_eq!(loaded.refresh_token, "refresh");
        // PascalCase on disk
        let raw = store.get(TWITCH_TOKEN_SET_KEY).unwrap().unwrap();
        assert!(raw.contains("\"AccessToken\""));
        assert!(raw.contains("\"RefreshToken\""));
        repo.delete().unwrap();
        assert!(repo.load().unwrap().is_none());
    }

    #[test]
    fn helix_users_url_encodes_login() {
        let url = helix_users_url("broadcaster / id");
        assert!(
            url.contains("login=broadcaster+%2F+id")
                || url.contains("login=broadcaster%20%2F%20id")
        );
        assert!(url.starts_with("https://api.twitch.tv/helix/users?login="));
    }

    #[tokio::test]
    async fn helix_current_user_maps_schema_and_required_headers() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/users"))
            .and(header("Authorization", "Bearer contract-token"))
            .and(header("Client-Id", "contract-client"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("users.json")))
            .mount(&server)
            .await;

        let client = TwitchHelixClient::with_base_url(
            format!("{}/", server.uri()),
            "contract-client",
            "contract-token",
        );
        let user = client.get_current_user().await.unwrap();
        assert_eq!(user.id, "141981764");
        assert_eq!(user.login, "twitchdev");
        assert_eq!(user.display_name, "TwitchDev");
        assert_eq!(
            user.profile_image_url,
            "https://static-cdn.jtvnw.net/profile.png"
        );
    }

    #[tokio::test]
    async fn helix_error_preserves_status_and_message() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/users"))
            .respond_with(ResponseTemplate::new(401).set_body_string(fixture("error.json")))
            .mount(&server)
            .await;

        let client = TwitchHelixClient::with_base_url(
            format!("{}/", server.uri()),
            "contract-client",
            "contract-token",
        );
        let err = client.get_current_user().await.unwrap_err().to_string();
        assert!(err.contains("Twitch API 401"));
        assert!(err.contains("OAuth token is not valid"));
    }

    #[tokio::test]
    async fn oauth_device_start_posts_form() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/oauth2/device"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("device.json")))
            .mount(&server)
            .await;

        let oauth = TwitchOAuthClient::with_base_urls(
            format!("{}/oauth2/device", server.uri()),
            format!("{}/oauth2/token", server.uri()),
            format!("{}/oauth2/validate", server.uri()),
        );
        let device = oauth
            .start_device_authorization(&contract_client_id(), &["user:read:chat".into()])
            .await
            .unwrap();
        assert_eq!(device.user_code, "ABCD-EFGH");
        assert_eq!(device.device_code, "device-code-value");
        assert!(device.verification_uri.contains("twitch.tv/activate"));
    }

    #[tokio::test]
    async fn oauth_poll_pending_then_token() {
        let server = MockServer::start().await;
        let calls = Arc::new(AtomicUsize::new(0));

        struct PendingThenToken {
            calls: Arc<AtomicUsize>,
            token_body: String,
        }
        impl wiremock::Respond for PendingThenToken {
            fn respond(&self, _: &wiremock::Request) -> ResponseTemplate {
                let n = self.calls.fetch_add(1, AtomicOrdering::SeqCst);
                if n == 0 {
                    ResponseTemplate::new(400).set_body_json(json!({
                        "status": 400,
                        "message": "authorization_pending"
                    }))
                } else {
                    ResponseTemplate::new(200).set_body_string(self.token_body.clone())
                }
            }
        }

        Mock::given(method("POST"))
            .and(path("/oauth2/token"))
            .respond_with(PendingThenToken {
                calls: Arc::clone(&calls),
                token_body: fixture("token.json"),
            })
            .mount(&server)
            .await;

        let oauth = TwitchOAuthClient::with_base_urls(
            format!("{}/oauth2/device", server.uri()),
            format!("{}/oauth2/token", server.uri()),
            format!("{}/oauth2/validate", server.uri()),
        );
        let device = TwitchDeviceCode {
            device_code: "device-code-value".into(),
            user_code: "ABCD-EFGH".into(),
            verification_uri: "https://www.twitch.tv/activate".into(),
            expires_in_seconds: 30,
            poll_interval_seconds: 1,
        };
        let token = oauth
            .wait_for_device_authorization(&contract_client_id(), &device)
            .await
            .unwrap();
        assert_eq!(token.access_token, "contract-access");
        assert_eq!(token.refresh_token, "contract-refresh");
        assert!(calls.load(AtomicOrdering::SeqCst) >= 2);
    }

    #[tokio::test]
    async fn oauth_validate_uses_oauth_header() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/oauth2/validate"))
            .and(header("Authorization", "OAuth contract-access"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("validate.json")))
            .mount(&server)
            .await;

        let oauth = TwitchOAuthClient::with_base_urls(
            format!("{}/oauth2/device", server.uri()),
            format!("{}/oauth2/token", server.uri()),
            format!("{}/oauth2/validate", server.uri()),
        );
        let v = oauth.validate("contract-access").await.unwrap();
        assert_eq!(v.login, "twitchdev");
        assert_eq!(v.user_id, "141981764");
    }

    #[tokio::test]
    async fn oauth_refresh_saves_rotated_token() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/oauth2/token"))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({
                "access_token": "new-access",
                "refresh_token": "new-refresh",
                "expires_in": 7200,
                "scope": ["user:read:chat"],
                "token_type": "bearer"
            })))
            .mount(&server)
            .await;

        struct ValidateByToken;
        impl wiremock::Respond for ValidateByToken {
            fn respond(&self, req: &wiremock::Request) -> ResponseTemplate {
                let auth = req
                    .headers
                    .get("Authorization")
                    .and_then(|v| v.to_str().ok())
                    .unwrap_or("");
                if auth == "OAuth new-access" {
                    ResponseTemplate::new(200).set_body_string(fixture("validate.json"))
                } else {
                    ResponseTemplate::new(401).set_body_json(json!({
                        "message": "invalid token"
                    }))
                }
            }
        }
        Mock::given(method("GET"))
            .and(path("/oauth2/validate"))
            .respond_with(ValidateByToken)
            .mount(&server)
            .await;

        Mock::given(method("GET"))
            .and(path_regex("/users.*"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("users.json")))
            .mount(&server)
            .await;

        let store = Arc::new(MemorySecretStore::new());
        let oauth = TwitchOAuthClient::with_base_urls(
            format!("{}/oauth2/device", server.uri()),
            format!("{}/oauth2/token", server.uri()),
            format!("{}/oauth2/validate", server.uri()),
        );
        let client = TwitchClient::with_http(store.clone(), oauth, format!("{}/", server.uri()));
        let old = TwitchTokenSet::from_oauth(
            "old-access".into(),
            "old-refresh".into(),
            60,
            vec!["user:read:chat".into()],
        );
        client.tokens.save(&old).unwrap();

        let status = client
            .connect(&TwitchConnectOptions {
                client_id: contract_client_id(),
                channel_name: String::new(),
                scopes: vec!["user:read:chat".into()],
                enable_event_sub: false,
            })
            .await
            .unwrap();
        assert_eq!(status.state, ConnectionState::Connected);
        let saved = client.tokens.load().unwrap().unwrap();
        assert_eq!(saved.access_token, "new-access");
        assert_eq!(saved.refresh_token, "new-refresh");
    }

    #[tokio::test]
    async fn login_happy_path_stores_token_and_connects() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/oauth2/device"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("device.json")))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/oauth2/token"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("token.json")))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/oauth2/validate"))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("validate.json")))
            .mount(&server)
            .await;
        Mock::given(method("GET"))
            .and(path("/users"))
            .and(header("Authorization", "Bearer contract-access"))
            .and(header("Client-Id", contract_client_id().as_str()))
            .respond_with(ResponseTemplate::new(200).set_body_string(fixture("users.json")))
            .mount(&server)
            .await;

        let store = Arc::new(MemorySecretStore::new());
        let oauth = TwitchOAuthClient::with_base_urls(
            format!("{}/oauth2/device", server.uri()),
            format!("{}/oauth2/token", server.uri()),
            format!("{}/oauth2/validate", server.uri()),
        );
        let client = Arc::new(TwitchClient::with_http(
            store.clone(),
            oauth,
            format!("{}/", server.uri()),
        ));

        let (status, uri) = client
            .begin_login(TwitchConnectOptions {
                client_id: contract_client_id(),
                channel_name: String::new(),
                scopes: vec!["user:read:chat".into()],
                enable_event_sub: false,
            })
            .await
            .unwrap();
        assert_eq!(status.state, ConnectionState::Connecting);
        assert!(status.detail.contains("ABCD-EFGH"));
        assert!(uri.contains("activate"));

        // Wait for poll task (interval 1s from fixture)
        for _ in 0..40 {
            tokio::time::sleep(std::time::Duration::from_millis(250)).await;
            if client.status().await.state == ConnectionState::Connected {
                break;
            }
        }
        let final_status = client.status().await;
        assert_eq!(final_status.state, ConnectionState::Connected);
        assert!(final_status.detail.contains("TwitchDev"));
        assert!(store.get(TWITCH_TOKEN_SET_KEY).unwrap().is_some());

        let after_logout = client.logout().await.unwrap();
        assert_eq!(after_logout.state, ConnectionState::Disconnected);
        assert!(store.get(TWITCH_TOKEN_SET_KEY).unwrap().is_none());
    }

    #[test]
    fn rejects_placeholder_client_id() {
        let err = TwitchOAuthClient::validate_client_id("your_client_id_placeholder").unwrap_err();
        assert!(err.to_string().contains("ungültig"));
    }
}
