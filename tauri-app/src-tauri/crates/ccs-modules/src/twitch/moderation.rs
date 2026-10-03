use super::TwitchClient;
use crate::overlay_bridge::{OverlayEventBridge, OverlayRealtimeEvent};
use ccs_core::JsonSettingsStore;
use chrono::{Local, Utc};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
    sync::Arc,
    time::Duration,
};
use tokio::{io::AsyncWriteExt, sync::Mutex};

#[derive(Debug, Deserialize)]
#[serde(
    tag = "action",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum ModerationAction {
    Timeout {
        user: String,
        by_id: bool,
        minutes: u32,
        reason: String,
    },
    Ban {
        user: String,
        by_id: bool,
        reason: String,
    },
    Unban {
        user: String,
        by_id: bool,
    },
    DeleteMessage {
        message_id: String,
    },
    ClearChat,
}
pub(super) struct ModerationDetails {
    pub kind: &'static str,
    pub user: String,
    pub by_id: bool,
    pub duration: Option<u32>,
    pub reason: String,
    pub message_id: Option<String>,
}
impl ModerationAction {
    pub(super) fn details(&self) -> Result<ModerationDetails, String> {
        let (kind, user, by_id, duration, reason, message_id) = match self {
            Self::Timeout {
                user,
                by_id,
                minutes,
                reason,
            } => {
                if *minutes == 0 || *minutes > i32::MAX as u32 {
                    return Err("Timeout muss mindestens eine ganze Minute betragen.".into());
                }
                (
                    "TIMEOUT",
                    user.as_str(),
                    *by_id,
                    Some((u64::from(*minutes) * 60).min(1_209_600) as u32),
                    reason.as_str(),
                    None,
                )
            }
            Self::Ban {
                user,
                by_id,
                reason,
            } => ("BAN", user.as_str(), *by_id, None, reason.as_str(), None),
            Self::Unban { user, by_id } => ("AUFHEBEN", user.as_str(), *by_id, None, "", None),
            Self::DeleteMessage { message_id } => {
                if message_id.trim().is_empty() {
                    return Err(
                        "Nachrichten-ID fehlt; Chat-Leerung muss ausdrücklich gewählt werden."
                            .into(),
                    );
                }
                (
                    "LÖSCHEN",
                    "",
                    false,
                    None,
                    "",
                    Some(message_id.trim().to_string()),
                )
            }
            Self::ClearChat => ("CHAT LEEREN", "", false, None, "", None),
        };
        let user = if by_id {
            user.trim()
        } else {
            user.trim().trim_start_matches('@')
        };
        if matches!(kind, "TIMEOUT" | "BAN" | "AUFHEBEN")
            && (user.is_empty() || user.chars().count() > 200)
        {
            return Err("Bitte einen Twitch-Benutzer auswählen oder eingeben.".into());
        }
        if reason.trim().chars().count() > 500 {
            return Err("Der Moderationsgrund darf höchstens 500 Zeichen enthalten.".into());
        }
        Ok(ModerationDetails {
            kind,
            user: user.into(),
            by_id,
            duration,
            reason: reason.trim().into(),
            message_id,
        })
    }
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ModerationSnapshot {
    pub entries: Vec<String>,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ModerationResult {
    pub applied: bool,
    pub message: String,
    pub warnings: Vec<String>,
    #[serde(skip)]
    pub event: Option<Value>,
}
pub struct ModerationRuntime {
    settings: Arc<JsonSettingsStore>,
    twitch: Arc<TwitchClient>,
    bridge: OverlayEventBridge,
    file: PathBuf,
    entries: Mutex<Vec<String>>,
    operation: Mutex<()>,
}
impl ModerationRuntime {
    pub fn new(
        settings: Arc<JsonSettingsStore>,
        twitch: Arc<TwitchClient>,
        bridge: OverlayEventBridge,
        logs: impl AsRef<Path>,
    ) -> Self {
        Self {
            settings,
            twitch,
            bridge,
            file: logs.as_ref().join("twitch-moderation.log"),
            entries: Mutex::new(vec![]),
            operation: Mutex::new(()),
        }
    }
    pub fn bridge(&self) -> &OverlayEventBridge {
        &self.bridge
    }
    pub async fn snapshot(&self) -> ModerationSnapshot {
        ModerationSnapshot {
            entries: self.entries.lock().await.clone(),
        }
    }
    pub async fn clear_view(&self) {
        let _guard = self.operation.lock().await;
        self.entries.lock().await.clear();
    }
    async fn append(
        &self,
        details: &ModerationDetails,
        result: &str,
        failed: bool,
    ) -> Result<(), String> {
        let single = |value: &str| value.replace(['\r', '\n'], " ");
        let target = details
            .message_id
            .as_ref()
            .map(|id| format!(" · Nachricht {}", single(id)))
            .unwrap_or_else(|| {
                if details.user.is_empty() {
                    String::new()
                } else {
                    format!(" · @{}", single(&details.user))
                }
            });
        let reason = if details.reason.is_empty() {
            String::new()
        } else {
            format!(" · Grund: {}", single(&details.reason))
        };
        let line = format!(
            "{} · {}{}{}{} · {}",
            Local::now().format("%d.%m.%Y %H:%M:%S"),
            details.kind,
            if failed { " FEHLER" } else { "" },
            target,
            reason,
            single(result)
        );
        {
            let mut entries = self.entries.lock().await;
            entries.insert(0, line.clone());
            entries.truncate(100);
        }
        tokio::fs::create_dir_all(self.file.parent().unwrap())
            .await
            .map_err(|e| e.to_string())?;
        let mut file = tokio::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&self.file)
            .await
            .map_err(|e| e.to_string())?;
        if file.metadata().await.map_err(|e| e.to_string())?.len() == 0 {
            file.write_all(&[0xef, 0xbb, 0xbf])
                .await
                .map_err(|e| e.to_string())?;
        }
        let newline = if cfg!(windows) { "\r\n" } else { "\n" };
        file.write_all(format!("{line}{newline}").as_bytes())
            .await
            .map_err(|e| e.to_string())?;
        file.flush().await.map_err(|e| e.to_string())
    }
    pub async fn export(&self, path: &Path) -> Result<(), String> {
        let _guard = self.operation.lock().await;
        let source = tokio::fs::canonicalize(&self.file)
            .await
            .map_err(|e| format!("Moderationsprotokoll ist nicht verfügbar: {e}"))?;
        if tokio::fs::canonicalize(path).await.ok().as_ref() == Some(&source) {
            return Err("Das aktive Moderationsprotokoll kann nicht überschrieben werden.".into());
        }
        let temp = path.with_file_name(format!(".twitch-moderation-{}.tmp", uuid::Uuid::new_v4()));
        let result = async {
            tokio::fs::copy(&self.file, &temp).await?;
            tokio::fs::rename(&temp, path).await
        }
        .await;
        if let Err(error) = result {
            let _ = tokio::fs::remove_file(&temp).await;
            return Err(format!("Protokoll konnte nicht exportiert werden: {error}"));
        }
        Ok(())
    }
    pub async fn execute(&self, action: ModerationAction) -> Result<ModerationResult, String> {
        let details = action.details()?;
        let _guard = self.operation.lock().await;
        let result=tokio::time::timeout(Duration::from_secs(12),async {
            let settings=self.settings.load().await.map_err(|e|e.to_string())?;
            self.twitch.perform_moderation(&settings.twitch.client_id,&settings.twitch.channel_name,&details).await.map_err(|e|e.to_string())
        }).await.unwrap_or_else(|_|Err("Twitch-Moderation hat das Zeitlimit überschritten; Ergebnis bitte auf Twitch prüfen.".into()));
        let target = match result {
            Ok(target) => target,
            Err(error) => {
                let log = self.append(&details, &error, true).await;
                return Err(match log {
                    Ok(()) => error,
                    Err(log) => format!(
                        "{error}; Moderationsprotokoll konnte nicht gespeichert werden: {log}"
                    ),
                });
            }
        };
        let message = match details.kind {
            "TIMEOUT" => format!(
                "{} erhielt einen Timeout von {} Minuten.",
                details.user,
                details.duration.unwrap() / 60
            ),
            "BAN" => format!("{} wurde gebannt.", details.user),
            "AUFHEBEN" => format!("Ban oder Timeout für {} wurde aufgehoben.", details.user),
            "LÖSCHEN" => "Die Nachricht wurde gelöscht.".into(),
            _ => "Der Twitch-Chat wurde geleert.".into(),
        };
        let cleanup = match details.kind {
            "TIMEOUT" | "BAN" => Some((
                "channel.chat.clear_user_messages",
                BTreeMap::from([
                    ("target_user_id".into(), target),
                    (
                        "target_user_login".into(),
                        if details.by_id {
                            String::new()
                        } else {
                            details.user.clone()
                        },
                    ),
                ]),
            )),
            "LÖSCHEN" => Some((
                "channel.chat.message_delete",
                BTreeMap::from([("message_id".into(), details.message_id.clone().unwrap())]),
            )),
            "CHAT LEEREN" => Some(("channel.chat.clear", BTreeMap::new())),
            _ => None,
        };
        let event = cleanup.map(|(ty, data)| {
            self.bridge.publish(&OverlayRealtimeEvent::new(
                "app",
                ty,
                Utc::now(),
                &message,
                data,
            ))
        });
        let warnings=self.append(&details,&message,false).await.err().map(|e|vec![format!("Twitch-Aktion erfolgreich; Moderationsprotokoll konnte nicht gespeichert werden: {e}")]).unwrap_or_default();
        Ok(ModerationResult {
            applied: true,
            message,
            warnings,
            event,
        })
    }
}
