use keyring::Entry;
use std::collections::HashMap;
use std::sync::Mutex;

const SERVICE: &str = "CastingCouch";

/// Logical key for the Twitch OAuth token blob (WPF-compatible).
pub const TWITCH_TOKEN_SET_KEY: &str = "twitch.tokenSet";

/// Logical key for the Spotify OAuth token blob (WPF-compatible).
pub const SPOTIFY_TOKEN_SET_KEY: &str = "spotify.tokenSet";

#[derive(Debug, thiserror::Error)]
pub enum SecretError {
    #[error("keyring: {0}")]
    Keyring(#[from] keyring::Error),
}

/// Abstraction over OS keyring / in-memory stores for tokens and passwords.
pub trait SecretStore: Send + Sync {
    fn set(&self, key: &str, value: &str) -> Result<(), SecretError>;
    fn get(&self, key: &str) -> Result<Option<String>, SecretError>;
    fn delete(&self, key: &str) -> Result<(), SecretError>;
}

pub struct KeyringSecretStore {
    prefix: String,
}

impl KeyringSecretStore {
    pub fn new() -> Self {
        Self {
            prefix: "ccs".into(),
        }
    }

    fn entry(&self, key: &str) -> Result<Entry, SecretError> {
        Ok(Entry::new(SERVICE, &format!("{}:{}", self.prefix, key))?)
    }
}

impl Default for KeyringSecretStore {
    fn default() -> Self {
        Self::new()
    }
}

impl SecretStore for KeyringSecretStore {
    fn set(&self, key: &str, value: &str) -> Result<(), SecretError> {
        self.entry(key)?.set_password(value)?;
        Ok(())
    }

    fn get(&self, key: &str) -> Result<Option<String>, SecretError> {
        match self.entry(key)?.get_password() {
            Ok(v) => Ok(Some(v)),
            Err(keyring::Error::NoEntry) => Ok(None),
            Err(e) => Err(e.into()),
        }
    }

    fn delete(&self, key: &str) -> Result<(), SecretError> {
        match self.entry(key)?.delete_credential() {
            Ok(()) => Ok(()),
            Err(keyring::Error::NoEntry) => Ok(()),
            Err(e) => Err(e.into()),
        }
    }
}

/// In-memory secret store for unit tests.
#[derive(Default)]
pub struct MemorySecretStore {
    inner: Mutex<HashMap<String, String>>,
}

impl MemorySecretStore {
    pub fn new() -> Self {
        Self::default()
    }
}

impl SecretStore for MemorySecretStore {
    fn set(&self, key: &str, value: &str) -> Result<(), SecretError> {
        self.inner
            .lock()
            .expect("memory secret store lock")
            .insert(key.to_string(), value.to_string());
        Ok(())
    }

    fn get(&self, key: &str) -> Result<Option<String>, SecretError> {
        Ok(self
            .inner
            .lock()
            .expect("memory secret store lock")
            .get(key)
            .cloned())
    }

    fn delete(&self, key: &str) -> Result<(), SecretError> {
        self.inner
            .lock()
            .expect("memory secret store lock")
            .remove(key);
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn memory_store_roundtrip_and_delete() {
        let store = MemorySecretStore::new();
        store
            .set(TWITCH_TOKEN_SET_KEY, r#"{"AccessToken":"a"}"#)
            .unwrap();
        assert_eq!(
            store.get(TWITCH_TOKEN_SET_KEY).unwrap().as_deref(),
            Some(r#"{"AccessToken":"a"}"#)
        );
        store.delete(TWITCH_TOKEN_SET_KEY).unwrap();
        assert_eq!(store.get(TWITCH_TOKEN_SET_KEY).unwrap(), None);
    }

    #[test]
    fn roundtrip_optional_on_ci() {
        let store = KeyringSecretStore::new();
        let key = format!("test-{}", uuid::Uuid::new_v4());
        match store.set(&key, "secret-value") {
            Ok(()) => {
                assert_eq!(store.get(&key).unwrap().as_deref(), Some("secret-value"));
                store.delete(&key).unwrap();
                assert_eq!(store.get(&key).unwrap(), None);
            }
            Err(_) => {
                // Headless CI may lack a credential store.
            }
        }
    }
}
