//! C#-compatible, bounded app journal. Recording is best effort; explicit edits are transactional.
use crate::{stream_end::StreamEndSnapshot, ConnectionState, ServiceStatus};
use chrono::{DateTime, Local, Utc};
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};
use std::{
    collections::{HashMap, HashSet},
    fs,
    io::{Read, Write},
    path::{Path, PathBuf},
    sync::Mutex,
};

const LIMIT: usize = 250;
const MAX_BYTES: u64 = 16 * 1024 * 1024;

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
struct Entry {
    timestamp: String,
    severity: String,
    message: String,
    is_read: bool,
    #[serde(flatten)]
    extra: Map<String, Value>,
}
impl Default for Entry {
    fn default() -> Self {
        Self {
            timestamp: "0001-01-01T00:00:00+00:00".into(),
            severity: "Info".into(),
            message: String::new(),
            is_read: false,
            extra: Map::new(),
        }
    }
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NotificationItem {
    pub timestamp: String,
    pub severity: String,
    pub message: String,
    pub is_read: bool,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NotificationSnapshot {
    pub entries: Vec<NotificationItem>,
    pub total: usize,
    pub unread_count: usize,
    pub warnings: Vec<String>,
    pub recovery_backup: Option<String>,
}
#[derive(Default)]
struct State {
    entries: Vec<Entry>,
    load_error: Option<String>,
    save_error: Option<String>,
    recovery_backup: Option<String>,
    stream: Option<(bool, DateTime<Utc>)>,
    services: HashMap<String, (ConnectionState, String)>,
    end_phase: Option<(u64, String)>,
    end_messages: HashSet<String>,
}
pub struct NotificationRuntime {
    path: PathBuf,
    state: Mutex<State>,
    changes: tokio::sync::broadcast::Sender<()>,
}
impl NotificationRuntime {
    pub fn new(root: impl AsRef<Path>) -> Self {
        let path = root.as_ref().join("notifications.json");
        let state = match load(&path) {
            Ok(entries) => State { entries, ..State::default() },
            Err(error) => State { load_error: Some(format!("Benachrichtigungen konnten nicht geladen werden: {error}. Die Datei bleibt erhalten; neue Meldungen liegen bis zur Reparatur nur im Arbeitsspeicher.")), ..State::default() },
        };
        Self {
            path,
            state: Mutex::new(state),
            changes: tokio::sync::broadcast::channel(64).0,
        }
    }
    pub fn subscribe_changes(&self) -> tokio::sync::broadcast::Receiver<()> {
        self.changes.subscribe()
    }
    pub fn snapshot(&self, filter: &str) -> Result<NotificationSnapshot, String> {
        let severity = match filter {
            "Alle" => None,
            "Info" => Some("Info"),
            "Warnungen" => Some("Warnung"),
            "Fehler" => Some("Fehler"),
            _ => return Err("Unbekannter Benachrichtigungsfilter".into()),
        };
        let state = self.state.lock().unwrap();
        let mut entries = state
            .entries
            .iter()
            .filter(|entry| severity.is_none_or(|s| entry.severity == s))
            .collect::<Vec<_>>();
        // Timestamps were validated on load. Keep the original offset/string on disk.
        entries.sort_by_key(|entry| {
            std::cmp::Reverse(DateTime::parse_from_rfc3339(&entry.timestamp).unwrap())
        });
        Ok(NotificationSnapshot {
            entries: entries
                .into_iter()
                .take(100)
                .map(|entry| NotificationItem {
                    timestamp: entry.timestamp.clone(),
                    severity: entry.severity.clone(),
                    message: entry.message.clone(),
                    is_read: entry.is_read,
                })
                .collect(),
            total: state.entries.len(),
            unread_count: state.entries.iter().filter(|entry| !entry.is_read).count(),
            warnings: state
                .load_error
                .iter()
                .chain(state.save_error.iter())
                .cloned()
                .collect(),
            recovery_backup: state.recovery_backup.clone(),
        })
    }
    pub fn record(&self, message: &str, severity: &str) {
        let mut state = self.state.lock().unwrap();
        self.append(&mut state, message, severity, Local::now().to_rfc3339());
    }
    fn append(&self, state: &mut State, message: &str, severity: &str, timestamp: String) {
        let severity = match severity {
            "Error" | "Fehler" => "Fehler",
            "Warning" | "Warnung" => "Warnung",
            _ => "Info",
        };
        state.entries.push(Entry {
            timestamp,
            severity: severity.into(),
            message: message.into(),
            ..Entry::default()
        });
        cap(&mut state.entries);
        if state.load_error.is_none() {
            state.save_error = self.save(&state.entries).err();
        }
        let _ = self.changes.send(());
    }
    fn save(&self, entries: &[Entry]) -> Result<(), String> {
        let bytes = serde_json::to_vec_pretty(entries).map_err(|e| e.to_string())?;
        crate::stream_history::checkpoint(&self.path, &bytes)
            .map_err(|e| format!("Benachrichtigungen wurden nicht gespeichert: {e}"))
    }
    fn edit(&self, change: impl FnOnce(&mut Vec<Entry>)) -> Result<(), String> {
        let mut state = self.state.lock().unwrap();
        if let Some(error) = &state.load_error {
            return Err(format!("{error} Zuerst Speichern erneut versuchen."));
        }
        let mut next = state.entries.clone();
        change(&mut next);
        let result = self.save(&next);
        state.save_error = result.as_ref().err().cloned();
        if result.is_ok() {
            state.entries = next;
        }
        let _ = self.changes.send(());
        result
    }
    pub fn mark_all_read(&self) -> Result<(), String> {
        self.edit(|entries| {
            for entry in entries {
                entry.is_read = true;
            }
        })
    }
    pub fn clear(&self) -> Result<(), String> {
        self.edit(Vec::clear)
    }
    pub fn retry(&self) -> Result<(), String> {
        let mut state = self.state.lock().unwrap();
        let result = (|| {
            let mut next = state.entries.clone();
            if state.load_error.is_some() {
                // Retry reading first: an externally repaired cache must not be overwritten.
                match read(&self.path)? {
                    None => {}
                    Some(bytes) => match parse(&bytes) {
                        Ok(mut original) => {
                            original.extend(next);
                            next = original;
                            cap(&mut next);
                        }
                        Err(_) => {
                            let backup = self.path.with_file_name(format!(
                                "notifications-corrupt-{}.json",
                                uuid::Uuid::new_v4()
                            ));
                            let mut output = fs::OpenOptions::new()
                                .write(true)
                                .create_new(true)
                                .open(&backup)
                                .map_err(|e| e.to_string())?;
                            output
                                .write_all(&bytes)
                                .and_then(|_| output.sync_all())
                                .map_err(|e| e.to_string())?;
                            state.recovery_backup = Some(backup.to_string_lossy().into_owned());
                        }
                    },
                }
            }
            self.save(&next)?;
            state.entries = next;
            state.load_error = None;
            Ok::<_, String>(())
        })();
        state.save_error = result.as_ref().err().cloned();
        let _ = self.changes.send(());
        result
    }
    pub fn observe_stream(&self, active: bool, at: DateTime<Utc>) {
        let mut state = self.state.lock().unwrap();
        if state.stream.is_some_and(|(_, previous)| at < previous) {
            return;
        }
        let previous = state.stream.replace((active, at));
        if previous.is_some_and(|(was_active, _)| was_active == active)
            || (previous.is_none() && !active)
        {
            return;
        }
        self.append(
            &mut state,
            if active {
                "OBS-Stream gestartet."
            } else {
                "OBS-Stream beendet."
            },
            "Info",
            at.with_timezone(&Local).to_rfc3339(),
        );
    }
    pub fn observe_service(&self, status: &ServiceStatus) {
        let mut state = self.state.lock().unwrap();
        let next = (status.state, status.detail.clone());
        let previous = state.services.insert(status.id.clone(), next.clone());
        if previous.as_ref() == Some(&next) {
            return;
        }
        let (label, severity) = match status.state {
            ConnectionState::Connecting => return,
            ConnectionState::Disconnected if previous.is_none() => return,
            ConnectionState::Disconnected => ("getrennt", "Warnung"),
            ConnectionState::Connected
                if previous.is_some_and(|(s, _)| s == ConnectionState::Connected) =>
            {
                return
            }
            ConnectionState::Connected => ("verbunden", "Info"),
            ConnectionState::Error => ("Verbindungsfehler", "Fehler"),
        };
        let detail = if status.detail.is_empty() {
            String::new()
        } else {
            format!(": {}", status.detail)
        };
        self.append(
            &mut state,
            &format!("{}: {label}{detail}", status.name),
            severity,
            Local::now().to_rfc3339(),
        );
    }
    pub fn observe_stream_end(&self, snapshot: &StreamEndSnapshot) {
        let mut state = self.state.lock().unwrap();
        if snapshot.run_id == 0 {
            return;
        }
        let key = (snapshot.run_id, snapshot.phase.clone());
        if state
            .end_phase
            .as_ref()
            .is_none_or(|(run, _)| *run != snapshot.run_id)
        {
            state.end_messages.clear();
        }
        if state.end_phase.as_ref() != Some(&key) {
            state.end_phase = Some(key);
            if snapshot.error.is_none() && !snapshot.status.is_empty() {
                self.append(
                    &mut state,
                    &format!("Streamende: {}", snapshot.status),
                    "Info",
                    Local::now().to_rfc3339(),
                );
            }
        }
        for (message, severity) in snapshot
            .error
            .iter()
            .map(|s| (s, "Fehler"))
            .chain(snapshot.warnings.iter().map(|s| (s, "Warnung")))
        {
            if state.end_messages.insert(format!("{severity}:{message}")) {
                self.append(
                    &mut state,
                    &format!("Streamende: {message}"),
                    severity,
                    Local::now().to_rfc3339(),
                );
            }
        }
    }
}
fn cap(entries: &mut Vec<Entry>) {
    if entries.len() > LIMIT {
        entries.drain(..entries.len() - LIMIT);
    }
}
fn read(path: &Path) -> Result<Option<Vec<u8>>, String> {
    let file = match fs::File::open(path) {
        Ok(file) => file,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => return Err(error.to_string()),
    };
    let mut bytes = Vec::new();
    file.take(MAX_BYTES + 1)
        .read_to_end(&mut bytes)
        .map_err(|e| e.to_string())?;
    if bytes.len() as u64 > MAX_BYTES {
        return Err("Benachrichtigungsdatei größer als 16 MiB; manuell sichern und prüfen".into());
    }
    Ok(Some(bytes))
}
fn parse(bytes: &[u8]) -> Result<Vec<Entry>, String> {
    let mut entries = serde_json::from_slice::<Option<Vec<Entry>>>(
        bytes.strip_prefix(b"\xef\xbb\xbf").unwrap_or(bytes),
    )
    .map_err(|e| e.to_string())?
    .unwrap_or_default();
    if entries
        .iter()
        .any(|entry| DateTime::parse_from_rfc3339(&entry.timestamp).is_err())
    {
        return Err("Ungültiger Zeitstempel".into());
    }
    cap(&mut entries);
    Ok(entries)
}
fn load(path: &Path) -> Result<Vec<Entry>, String> {
    read(path)?.map_or_else(|| Ok(Vec::new()), |bytes| parse(&bytes))
}
