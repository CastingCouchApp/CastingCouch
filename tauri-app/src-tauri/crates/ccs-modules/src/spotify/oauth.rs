use crate::{ModuleError, ModuleResult};
use base64::engine::general_purpose::URL_SAFE_NO_PAD;
use base64::Engine;
use serde::Deserialize;
use sha2::{Digest, Sha256};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpListener;
use tokio::time::timeout;

use super::tokens::SpotifyTokenSet;

pub const AUTHORIZE_URL: &str = "https://accounts.spotify.com/authorize";
pub const TOKEN_URL: &str = "https://accounts.spotify.com/api/token";
pub const DEFAULT_REDIRECT_URI: &str = "http://127.0.0.1:43821/callback/";
const LOGIN_TIMEOUT: Duration = Duration::from_secs(300);

#[derive(Clone)]
pub struct SpotifyOAuthClient {
    http: reqwest::Client,
    authorize_url: String,
    token_url: String,
}

pub struct PendingAuthorization {
    pub authorize_url: String,
    pub verifier: String,
    pub state: String,
    pub redirect_uri: String,
    listener: TcpListener,
}

impl Default for SpotifyOAuthClient {
    fn default() -> Self {
        Self::new()
    }
}

impl SpotifyOAuthClient {
    pub fn new() -> Self {
        Self {
            http: reqwest::Client::new(),
            authorize_url: AUTHORIZE_URL.into(),
            token_url: TOKEN_URL.into(),
        }
    }

    pub fn with_base_urls(
        authorize_url: impl Into<String>,
        token_url: impl Into<String>,
    ) -> Self {
        Self {
            http: reqwest::Client::new(),
            authorize_url: authorize_url.into(),
            token_url: token_url.into(),
        }
    }

    pub fn validate_client_id(client_id: &str) -> ModuleResult<()> {
        let value = client_id.trim();
        if value.is_empty() {
            return Err(ModuleError::Message(
                "Bitte zuerst die Spotify Client-ID eintragen.".into(),
            ));
        }
        let lower = value.to_ascii_lowercase();
        if lower.contains("your_client_id")
            || lower.contains("placeholder")
            || lower.contains("changeme")
        {
            return Err(ModuleError::Message(
                "Spotify Client-ID ist ungültig. Bitte unter Einstellungen eine gültige Client-ID der Spotify-Developer-App eintragen."
                    .into(),
            ));
        }
        Ok(())
    }

    pub fn create_code_challenge(verifier: &str) -> String {
        let hash = Sha256::digest(verifier.as_bytes());
        URL_SAFE_NO_PAD.encode(hash)
    }

    pub fn create_code_verifier() -> String {
        let mut bytes = [0u8; 64];
        getrandom::getrandom(&mut bytes).expect("rng");
        URL_SAFE_NO_PAD.encode(bytes)
    }

    pub fn create_state() -> String {
        let mut bytes = [0u8; 24];
        getrandom::getrandom(&mut bytes).expect("rng");
        hex_encode(&bytes)
    }

    pub fn build_authorization_uri(
        &self,
        client_id: &str,
        redirect_uri: &str,
        scopes: &[String],
        challenge: &str,
        state: &str,
    ) -> String {
        let scopes_joined = scopes.join(" ");
        let mut url = url::Url::parse(&self.authorize_url).unwrap_or_else(|_| {
            url::Url::parse(AUTHORIZE_URL).expect("spotify authorize url")
        });
        {
            let mut q = url.query_pairs_mut();
            q.append_pair("client_id", client_id);
            q.append_pair("response_type", "code");
            q.append_pair("redirect_uri", redirect_uri);
            q.append_pair("code_challenge_method", "S256");
            q.append_pair("code_challenge", challenge);
            q.append_pair("scope", &scopes_joined);
            q.append_pair("state", state);
            q.append_pair("show_dialog", "true");
        }
        url.to_string()
    }

    pub async fn start_authorization(
        &self,
        client_id: &str,
        redirect_uri: &str,
        scopes: &[String],
    ) -> ModuleResult<PendingAuthorization> {
        Self::validate_client_id(client_id)?;
        let listener = bind_loopback(redirect_uri).await?;
        let verifier = Self::create_code_verifier();
        let challenge = Self::create_code_challenge(&verifier);
        let state = Self::create_state();
        let authorize_url =
            self.build_authorization_uri(client_id, redirect_uri, scopes, &challenge, &state);
        Ok(PendingAuthorization {
            authorize_url,
            verifier,
            state,
            redirect_uri: redirect_uri.to_string(),
            listener,
        })
    }

    pub async fn wait_for_code(
        pending: PendingAuthorization,
        cancel: &AtomicBool,
    ) -> ModuleResult<String> {
        let expected_state = pending.state.clone();
        let listener = pending.listener;
        timeout(LOGIN_TIMEOUT, async {
            loop {
                if cancel.load(Ordering::SeqCst) {
                    return Err(ModuleError::Message(
                        "Spotify-Anmeldung wurde abgebrochen.".into(),
                    ));
                }
                let accept = timeout(Duration::from_secs(1), listener.accept()).await;
                match accept {
                    Ok(Ok((mut stream, _))) => {
                        let mut buf = vec![0u8; 8192];
                        let n = match timeout(Duration::from_secs(5), stream.read(&mut buf)).await {
                            Ok(Ok(n)) => n,
                            _ => continue,
                        };
                        let request = String::from_utf8_lossy(&buf[..n]);
                        let parsed = match parse_callback_request(&request) {
                            Some(p) => p,
                            None => {
                                let _ = write_html(
                                    &mut stream,
                                    "Ungültige Spotify-Autorisierungsantwort.",
                                    false,
                                )
                                .await;
                                continue;
                            }
                        };
                        if parsed.path.contains("favicon") {
                            let _ = write_raw(&mut stream, b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").await;
                            continue;
                        }
                        if let Some(error) = parsed.error {
                            let _ = write_html(
                                &mut stream,
                                "Spotify-Autorisierung wurde abgelehnt.",
                                false,
                            )
                            .await;
                            return Err(ModuleError::Message(format!(
                                "Spotify-Autorisierung wurde abgelehnt: {error}"
                            )));
                        }
                        if parsed.state != expected_state {
                            let _ = write_html(
                                &mut stream,
                                "Ungültige Spotify-Autorisierungsantwort.",
                                false,
                            )
                            .await;
                            return Err(ModuleError::Message(
                                "Der Spotify OAuth-State stimmt nicht überein.".into(),
                            ));
                        }
                        if parsed.code.is_empty() {
                            let _ = write_html(
                                &mut stream,
                                "Spotify hat keinen Autorisierungscode geliefert.",
                                false,
                            )
                            .await;
                            return Err(ModuleError::Message(
                                "Spotify hat keinen Autorisierungscode geliefert.".into(),
                            ));
                        }
                        let _ = write_html(
                            &mut stream,
                            "Spotify wurde verbunden. Dieses Browserfenster kann geschlossen werden.",
                            true,
                        )
                        .await;
                        return Ok(parsed.code);
                    }
                    Ok(Err(e)) => {
                        return Err(ModuleError::Io(e));
                    }
                    Err(_) => continue,
                }
            }
        })
        .await
        .map_err(|_| {
            ModuleError::Message("Die Spotify-Anmeldung ist abgelaufen. Bitte erneut versuchen.".into())
        })?
    }

    pub async fn exchange_code(
        &self,
        client_id: &str,
        redirect_uri: &str,
        code: &str,
        verifier: &str,
    ) -> ModuleResult<SpotifyTokenSet> {
        Self::validate_client_id(client_id)?;
        let response = self
            .http
            .post(&self.token_url)
            .form(&[
                ("client_id", client_id),
                ("grant_type", "authorization_code"),
                ("code", code),
                ("redirect_uri", redirect_uri),
                ("code_verifier", verifier),
            ])
            .send()
            .await?;
        let status = response.status();
        let body = response.text().await.unwrap_or_default();
        if !status.is_success() {
            return Err(map_oauth_error(status.as_u16(), &body));
        }
        parse_token_response(&body)
    }

    pub async fn refresh(&self, client_id: &str, refresh_token: &str) -> ModuleResult<SpotifyTokenSet> {
        Self::validate_client_id(client_id)?;
        if refresh_token.trim().is_empty() {
            return Err(ModuleError::Message(
                "Der Spotify-Token ist abgelaufen. Bitte Spotify neu autorisieren.".into(),
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
            return Err(map_oauth_error(status.as_u16(), &body));
        }
        parse_token_response(&body)
    }
}

struct CallbackRequest {
    path: String,
    code: String,
    state: String,
    error: Option<String>,
}

fn parse_callback_request(request: &str) -> Option<CallbackRequest> {
    let first = request.lines().next()?;
    let mut parts = first.split_whitespace();
    let _method = parts.next()?;
    let target = parts.next()?;
    let url = if target.starts_with("http") {
        url::Url::parse(target).ok()?
    } else {
        url::Url::parse(&format!("http://127.0.0.1{target}")).ok()?
    };
    let mut code = String::new();
    let mut state = String::new();
    let mut error = None;
    for (key, value) in url.query_pairs() {
        match key.as_ref() {
            "code" => code = value.into_owned(),
            "state" => state = value.into_owned(),
            "error" => error = Some(value.into_owned()),
            _ => {}
        }
    }
    Some(CallbackRequest {
        path: url.path().to_string(),
        code,
        state,
        error,
    })
}

async fn bind_loopback(redirect_uri: &str) -> ModuleResult<TcpListener> {
    let uri = url::Url::parse(redirect_uri).map_err(|_| {
        ModuleError::Message("Die Spotify Redirect-URI ist ungültig.".into())
    })?;
    if uri.host_str() != Some("127.0.0.1") {
        return Err(ModuleError::Message(
            "Die Spotify Redirect-URI muss 127.0.0.1 verwenden.".into(),
        ));
    }
    let port = uri.port().unwrap_or(80);
    TcpListener::bind(("127.0.0.1", port))
        .await
        .map_err(|e| ModuleError::Message(format!("Spotify-Callback-Port {port} ist belegt: {e}")))
}

async fn write_html(
    stream: &mut tokio::net::TcpStream,
    message: &str,
    is_success: bool,
) -> std::io::Result<()> {
    let color = if is_success { "#5CE06E" } else { "#E05C5C" };
    let encoded = html_encode(message);
    let body = format!(
        "<!doctype html><html><head><meta charset=\"utf-8\">\
         <title>CastingCouch</title></head>\
         <body style=\"font-family:Segoe UI;background:#101010;\
         color:white;display:grid;place-items:center;height:100vh\">\
         <div style=\"max-width:650px;padding:32px;border:1px solid #444;\
         border-radius:12px;background:#181818\">\
         <h1 style=\"color:{color}\">CastingCouch</h1>\
         <p style=\"font-size:18px\">{encoded}</p></div></body></html>"
    );
    let header = format!(
        "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n\
         Connection: close\r\nContent-Length: {}\r\n\r\n",
        body.len()
    );
    stream.write_all(header.as_bytes()).await?;
    stream.write_all(body.as_bytes()).await?;
    let _ = stream.flush().await;
    Ok(())
}

async fn write_raw(stream: &mut tokio::net::TcpStream, bytes: &[u8]) -> std::io::Result<()> {
    stream.write_all(bytes).await?;
    stream.flush().await
}

fn html_encode(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
}

fn hex_encode(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut out = String::with_capacity(bytes.len() * 2);
    for b in bytes {
        out.push(HEX[(b >> 4) as usize] as char);
        out.push(HEX[(b & 0x0f) as usize] as char);
    }
    out
}

fn parse_token_response(body: &str) -> ModuleResult<SpotifyTokenSet> {
    let token: TokenResponse = serde_json::from_str(body).map_err(|e| {
        ModuleError::Message(format!("Spotify Token-Antwort ungültig: {e}"))
    })?;
    if token.access_token.is_empty() {
        return Err(ModuleError::Message(
            "Spotify Token-Antwort war leer.".into(),
        ));
    }
    Ok(SpotifyTokenSet::from_oauth(
        token.access_token,
        token.refresh_token,
        token.expires_in,
        token.token_type,
        token.scope,
    ))
}

pub(crate) fn map_oauth_error(status: u16, body: &str) -> ModuleError {
    let message = parse_oauth_error_message(body);
    ModuleError::Message(format!("Spotify HTTP {status}: {message}"))
}

fn parse_oauth_error_message(body: &str) -> String {
    #[derive(Deserialize)]
    struct OAuthErr {
        #[serde(default)]
        error: String,
        #[serde(default)]
        error_description: String,
    }
    if let Ok(parsed) = serde_json::from_str::<OAuthErr>(body) {
        if !parsed.error_description.is_empty() {
            return parsed.error_description;
        }
        if !parsed.error.is_empty() && parsed.error != "invalid_grant" {
            // Prefer human-readable fields; never echo token-shaped JSON keys.
            if parsed.error != "access_token" && parsed.error != "refresh_token" {
                return parsed.error;
            }
        }
        if !parsed.error.is_empty() {
            return parsed.error;
        }
    }
    "Anfrage fehlgeschlagen.".into()
}

#[derive(Deserialize)]
struct TokenResponse {
    access_token: String,
    #[serde(default)]
    refresh_token: String,
    #[serde(default)]
    expires_in: i32,
    #[serde(default)]
    token_type: String,
    #[serde(default, deserialize_with = "deserialize_scopes")]
    scope: Vec<String>,
}

fn deserialize_scopes<'de, D>(deserializer: D) -> Result<Vec<String>, D::Error>
where
    D: serde::Deserializer<'de>,
{
    struct ScopesVisitor;
    impl<'de> serde::de::Visitor<'de> for ScopesVisitor {
        type Value = Vec<String>;

        fn expecting(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
            f.write_str("space-separated string or string array")
        }

        fn visit_str<E: serde::de::Error>(self, v: &str) -> Result<Self::Value, E> {
            Ok(v.split_whitespace()
                .filter(|s| !s.is_empty())
                .map(|s| s.to_string())
                .collect())
        }

        fn visit_seq<A: serde::de::SeqAccess<'de>>(
            self,
            mut seq: A,
        ) -> Result<Self::Value, A::Error> {
            let mut values = Vec::new();
            while let Some(item) = seq.next_element::<String>()? {
                if !item.is_empty() {
                    values.push(item);
                }
            }
            Ok(values)
        }

        fn visit_none<E>(self) -> Result<Self::Value, E> {
            Ok(Vec::new())
        }

        fn visit_unit<E>(self) -> Result<Self::Value, E> {
            Ok(Vec::new())
        }
    }
    deserializer.deserialize_any(ScopesVisitor)
}
