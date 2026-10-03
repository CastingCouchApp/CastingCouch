//! Stream end orchestration. A countdown is never evidence that Twitch completed a raid.
//! Mutating I/O is allowed to settle before queued cancellation is applied, so an accepted
//! POST cannot lose its result and accidentally be retried as a new raid.
use crate::twitch::{checked_raid_login, TwitchEvent};
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use serde_json::{json, Map, Value};
use std::{future::Future, pin::Pin, sync::Arc, time::Duration};
use tokio::{
    sync::{broadcast, mpsc, watch, Mutex},
    time::Instant,
};

const MODES: &[&str] = &["Immediate", "EndSceneThenStop", "EndSceneRaidThenStop"];

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct StreamEndPreferences {
    pub mode: String,
    pub end_scene_seconds: u32,
    pub raid_on_stream_end: bool,
    pub selected_raid_channel: String,
    pub raid_countdown_seconds: u32,
    pub raid_start_timeout_seconds: u32,
    pub stop_stream_after_raid: bool,
    pub stop_music_after_raid: bool,
    pub planned_seconds: u32,
    pub planned_minutes: u32,
}
impl Default for StreamEndPreferences {
    fn default() -> Self {
        Self {
            mode: "EndSceneThenStop".into(),
            end_scene_seconds: 60,
            raid_on_stream_end: false,
            selected_raid_channel: String::new(),
            raid_countdown_seconds: 90,
            raid_start_timeout_seconds: 120,
            stop_stream_after_raid: true,
            stop_music_after_raid: true,
            planned_seconds: 0,
            planned_minutes: 30,
        }
    }
}
fn section<'a>(original: &'a Value, key: &str) -> Result<Option<&'a Map<String, Value>>, String> {
    match original.get(key) {
        None => Ok(None),
        Some(Value::Object(v)) => Ok(Some(v)),
        _ => Err(format!("{key}: Einstellungsobjekt erwartet")),
    }
}
fn field<'a>(section: Option<&'a Map<String, Value>>, key: &str) -> Option<&'a Value> {
    section.and_then(|v| v.get(key))
}
fn integer(section: Option<&Map<String, Value>>, key: &str, default: i64) -> Result<i64, String> {
    field(section, key).map_or(Ok(default), |v| {
        v.as_i64()
            .ok_or_else(|| format!("{key}: Ganzzahl erwartet"))
    })
}
fn boolean(section: Option<&Map<String, Value>>, key: &str, default: bool) -> Result<bool, String> {
    field(section, key).map_or(Ok(default), |v| {
        v.as_bool()
            .ok_or_else(|| format!("{key}: Wahrheitswert erwartet"))
    })
}
fn text(section: Option<&Map<String, Value>>, key: &str, default: &str) -> Result<String, String> {
    field(section, key).map_or(Ok(default.into()), |v| {
        v.as_str()
            .map(str::to_owned)
            .ok_or_else(|| format!("{key}: Text erwartet"))
    })
}
impl StreamEndPreferences {
    pub fn read(original: &Value) -> Result<Self, String> {
        if !original.is_object() {
            return Err("Einstellungsobjekt erwartet".into());
        }
        let twitch = section(original, "Twitch")?;
        let workflow = section(original, "Workflow")?;
        let mode = match field(twitch, "StreamEndMode") {
            None => "EndSceneThenStop".into(),
            Some(Value::String(mode)) if MODES.contains(&mode.as_str()) => mode.clone(),
            Some(v) => v
                .as_u64()
                .and_then(|index| usize::try_from(index).ok())
                .and_then(|index| MODES.get(index))
                .map(|s| s.to_string())
                .ok_or("Unbekannter StreamEndMode")?,
        };
        let end = integer(twitch, "EndSceneDurationSeconds", 60)?;
        let end = if end > 0 {
            end
        } else {
            integer(workflow, "EndSceneSeconds", 60)?
        };
        let timeout = integer(twitch, "RaidStartTimeoutSeconds", 120)?;
        let result = Self {
            mode,
            end_scene_seconds: end.max(0).try_into().map_err(|_| "Endszene zu lang")?,
            raid_on_stream_end: boolean(twitch, "RaidOnStreamEnd", false)?,
            selected_raid_channel: text(twitch, "SelectedRaidChannel", "")?,
            raid_countdown_seconds: integer(twitch, "RaidCountdownSeconds", 90)?.clamp(5, 300)
                as u32,
            raid_start_timeout_seconds: if timeout <= 0 {
                120
            } else {
                timeout.clamp(15, 600) as u32
            },
            stop_stream_after_raid: boolean(twitch, "StopStreamAfterRaid", true)?,
            stop_music_after_raid: boolean(twitch, "StopSpotifyAfterRaid", true)?,
            planned_seconds: integer(twitch, "PlannedStreamEndSeconds", 0)?
                .max(0)
                .try_into()
                .map_err(|_| "Geplantes Streamende zu lang")?,
            planned_minutes: integer(twitch, "PlannedStreamEndMinutes", 30)?
                .max(1)
                .try_into()
                .map_err(|_| "Geplantes Streamende zu lang")?,
        };
        result.validate()?;
        Ok(result)
    }
    pub fn validate(&self) -> Result<(), String> {
        if !MODES.contains(&self.mode.as_str()) {
            return Err("Unbekannter StreamEndMode".into());
        }
        if self.end_scene_seconds > i32::MAX as u32
            || self.planned_seconds > i32::MAX as u32
            || !(1..=i32::MAX as u32).contains(&self.planned_minutes)
        {
            return Err("Countdown außerhalb des gültigen Bereichs".into());
        }
        if !(5..=300).contains(&self.raid_countdown_seconds)
            || !(15..=600).contains(&self.raid_start_timeout_seconds)
        {
            return Err("Raid-Countdown: 5–300 Sekunden; Start-Timeout: 15–600 Sekunden".into());
        }
        if !self.selected_raid_channel.trim().is_empty() {
            checked_raid_login(&self.selected_raid_channel).map_err(|e| e.to_string())?;
        }
        Ok(())
    }
    /// Change only edited fields, preserving C# numeric enums, legacy fallback values and
    /// unknown nested fields when the effective value did not change.
    pub fn apply(&self, original: &Value) -> Result<Value, String> {
        self.validate()?;
        let before = Self::read(original)?;
        let mut result = original.clone();
        let mut put = |section: &str, key: &str, value: Value| {
            let root = result.as_object_mut().unwrap();
            root.entry(section.to_string())
                .or_insert_with(|| json!({}))
                .as_object_mut()
                .unwrap()
                .insert(key.into(), value);
        };
        if self.mode != before.mode {
            let index = MODES.iter().position(|m| *m == self.mode).unwrap();
            put(
                "Twitch",
                "StreamEndMode",
                if original["Twitch"]["StreamEndMode"].is_number() {
                    json!(index)
                } else {
                    json!(self.mode)
                },
            );
        }
        if self.end_scene_seconds != before.end_scene_seconds {
            put(
                "Twitch",
                "EndSceneDurationSeconds",
                json!(self.end_scene_seconds),
            );
            put("Workflow", "EndSceneSeconds", json!(self.end_scene_seconds));
        }
        macro_rules! changed {
            ($member:ident,$key:literal) => {
                if self.$member != before.$member {
                    put("Twitch", $key, json!(self.$member));
                }
            };
        }
        changed!(raid_on_stream_end, "RaidOnStreamEnd");
        if self.selected_raid_channel != before.selected_raid_channel {
            let login = crate::twitch::normalize_raid_channel(&self.selected_raid_channel);
            put("Twitch", "SelectedRaidChannel", json!(login));
            if !login.is_empty() {
                let channels: Vec<String> = original["Twitch"]
                    .get("RaidChannels")
                    .map_or(Ok(vec![]), |v| serde_json::from_value(v.clone()))
                    .map_err(|_| "RaidChannels: Textliste erwartet")?;
                put(
                    "Twitch",
                    "RaidChannels",
                    json!(crate::twitch::remember_raid_channel(&channels, &login)),
                );
            }
        }
        changed!(raid_countdown_seconds, "RaidCountdownSeconds");
        changed!(raid_start_timeout_seconds, "RaidStartTimeoutSeconds");
        changed!(stop_stream_after_raid, "StopStreamAfterRaid");
        changed!(stop_music_after_raid, "StopSpotifyAfterRaid");
        changed!(planned_seconds, "PlannedStreamEndSeconds");
        changed!(planned_minutes, "PlannedStreamEndMinutes");
        Ok(result)
    }
    pub fn planned_mode(&self) -> &str {
        if self.raid_on_stream_end {
            "EndSceneRaidThenStop"
        } else {
            "EndSceneThenStop"
        }
    }
}

#[derive(Clone, Debug)]
pub struct StreamEndPlan {
    pub preferences: StreamEndPreferences,
    pub end_scene: String,
    pub start_scene: String,
    pub broadcaster_id: String,
    pub broadcaster_login: String,
    pub play_end_music: bool,
    pub pause_music_on_stream_end: bool,
}
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum StreamEndOperation {
    SetScene { scene: String },
    PlayEndMusic,
    PauseMusic,
    StopStream,
    ProbeRaid { login: String },
    StartRaid { login: String },
    CancelRaid,
    RaidNow { login: String },
    AcknowledgeRaid,
}
#[derive(Clone, Debug)]
pub struct RaidIdentity {
    pub id: String,
    pub login: String,
    pub display_name: String,
    pub online: bool,
}
pub enum StreamEndReply {
    Done,
    Target(Option<RaidIdentity>),
    Warnings(Vec<String>),
}
pub type IoFuture<'a> = Pin<Box<dyn Future<Output = Result<StreamEndReply, String>> + Send + 'a>>;
/// Adapter uses the same OBS/Twitch/music clients as the normal service commands.
/// Mutations must report ambiguous results, and never silently retry a Raid POST.
pub trait StreamEndIo: Send + Sync {
    fn execute(&self, operation: StreamEndOperation) -> IoFuture<'_>;
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StreamEndSnapshot {
    pub run_id: u64,
    pub active: bool,
    pub phase: String,
    pub status: String,
    pub remaining_seconds: u32,
    pub total_seconds: u32,
    pub attempt: u32,
    pub target_login: String,
    pub target_display_name: String,
    pub can_raid_now: bool,
    pub raid_pending: bool,
    pub pending_action: Option<String>,
    pub broadcaster_id: String,
    pub broadcaster_login: String,
    pub error: Option<String>,
    pub warnings: Vec<String>,
}
impl Default for StreamEndSnapshot {
    fn default() -> Self {
        Self {
            run_id: 0,
            active: false,
            phase: "idle".into(),
            status: "Kein Streamende geplant".into(),
            remaining_seconds: 0,
            total_seconds: 0,
            attempt: 0,
            target_login: String::new(),
            target_display_name: String::new(),
            can_raid_now: false,
            raid_pending: false,
            pending_action: None,
            broadcaster_id: String::new(),
            broadcaster_login: String::new(),
            error: None,
            warnings: vec![],
        }
    }
}
enum Message {
    Control(String),
    Event(TwitchEvent),
    ExternalStop,
}
#[derive(Default)]
struct RuntimeState {
    snapshot: StreamEndSnapshot,
    commands: Option<mpsc::Sender<Message>>,
    confirmations: Option<watch::Sender<Option<TwitchEvent>>>,
    expected: Option<RaidProof>,
}
struct RaidProof {
    from_id: String,
    from_login: String,
    to_id: String,
    to_login: String,
    requested_at: DateTime<Utc>,
}
impl RaidProof {
    fn matches(&self, event: &TwitchEvent) -> bool {
        if event.event_type != "channel.raid" || event.received_at < self.requested_at {
            return false;
        }
        if let Some(timestamp) = event.data.get("eventSubMessageTimestamp") {
            match DateTime::parse_from_rfc3339(timestamp) {
                Ok(at) if at.with_timezone(&Utc) >= self.requested_at => {}
                _ => return false,
            }
        }
        let matches = |id_key: &str, login_key: &str, id: &str, login: &str| {
            if let Some(actual) = event.data.get(id_key).filter(|s| !s.is_empty()) {
                return actual == id;
            }
            event
                .data
                .get(login_key)
                .is_some_and(|value| value.eq_ignore_ascii_case(login))
        };
        matches(
            "from_broadcaster_user_id",
            "from_broadcaster_user_login",
            &self.from_id,
            &self.from_login,
        ) && matches(
            "to_broadcaster_user_id",
            "to_broadcaster_user_login",
            &self.to_id,
            &self.to_login,
        )
    }
}
pub struct StreamEndRuntime {
    state: Mutex<RuntimeState>,
    changes: broadcast::Sender<StreamEndSnapshot>,
}
impl Default for StreamEndRuntime {
    fn default() -> Self {
        Self {
            state: Mutex::new(RuntimeState::default()),
            changes: broadcast::channel(64).0,
        }
    }
}
impl StreamEndRuntime {
    pub async fn snapshot(&self) -> StreamEndSnapshot {
        self.state.lock().await.snapshot.clone()
    }
    pub fn subscribe_changes(&self) -> broadcast::Receiver<StreamEndSnapshot> {
        self.changes.subscribe()
    }
    pub async fn start(
        self: &Arc<Self>,
        plan: StreamEndPlan,
        delay_seconds: u32,
        io: Arc<dyn StreamEndIo>,
    ) -> Result<StreamEndSnapshot, String> {
        plan.preferences.validate()?;
        if delay_seconds > i32::MAX as u32 {
            return Err("Geplantes Streamende außerhalb des C#-Zahlenbereichs".into());
        }
        let wants_raid = plan.preferences.mode == "EndSceneRaidThenStop"
            && !plan.preferences.selected_raid_channel.trim().is_empty();
        if wants_raid && (plan.broadcaster_id.is_empty() || plan.broadcaster_login.is_empty()) {
            return Err("Twitch-Kanal für die ausgehende Raid-Bestätigung fehlt".into());
        }
        if wants_raid
            && crate::twitch::normalize_raid_channel(&plan.preferences.selected_raid_channel)
                .eq_ignore_ascii_case(&plan.broadcaster_login)
        {
            return Err("Ein Raid zum eigenen Kanal ist nicht möglich".into());
        }
        let mut state = self.state.lock().await;
        if state.snapshot.active {
            return Err("Ein Streamende läuft bereits".into());
        }
        if state.snapshot.raid_pending {
            return Err("Vorherigen Raid zuerst in Twitch prüfen oder abbrechen".into());
        }
        let (tx, rx) = mpsc::channel(32);
        let (confirmations, confirmation_rx) = watch::channel(None);
        let snapshot = StreamEndSnapshot {
            run_id: state.snapshot.run_id.wrapping_add(1),
            active: true,
            phase: if delay_seconds > 0 {
                "scheduled"
            } else {
                "preparing"
            }
            .into(),
            status: "Streamende wird vorbereitet".into(),
            remaining_seconds: delay_seconds,
            total_seconds: delay_seconds,
            target_login: crate::twitch::normalize_raid_channel(
                &plan.preferences.selected_raid_channel,
            ),
            broadcaster_id: plan.broadcaster_id.clone(),
            broadcaster_login: plan.broadcaster_login.clone(),
            ..StreamEndSnapshot::default()
        };
        state.snapshot = snapshot.clone();
        state.commands = Some(tx);
        state.confirmations = Some(confirmations);
        state.expected = None;
        let _ = self.changes.send(snapshot.clone());
        let worker = Worker {
            runtime: self.clone(),
            io,
            plan,
            snapshot: snapshot.clone(),
            rx,
            confirmation_rx,
            abort: false,
            skip_raid: false,
            cancel_raid: false,
            raid_now: false,
            skip_end: false,
            start_now: false,
            external_stop: false,
            raid_confirmed: false,
            raid_requested_at: None,
            target: None,
            raid_started: None,
        };
        tokio::spawn(worker.run(delay_seconds));
        Ok(snapshot)
    }
    pub async fn control(&self, action: &str) -> Result<(), String> {
        let mut state = self.state.lock().await;
        let snapshot = &state.snapshot;
        let permitted = match action {
            "abort" => {
                snapshot.active && !matches!(snapshot.phase.as_str(), "stopping" | "finalizing")
            }
            "start_now" => snapshot.phase == "scheduled",
            "skip_end" => snapshot.phase == "end_scene",
            "skip_raid" => {
                snapshot.active
                    && matches!(
                        snapshot.phase.as_str(),
                        "raid_probe"
                            | "raid_retry"
                            | "raid_starting"
                            | "raid_countdown"
                            | "awaiting_raid"
                            | "raid_uncertain"
                    )
            }
            "cancel_raid" => snapshot.active && snapshot.raid_pending,
            "raid_now" => snapshot.active && snapshot.can_raid_now,
            _ => false,
        };
        if !permitted {
            return Err("Aktion ist in diesem Streamende-Zustand nicht verfügbar".into());
        }
        state
            .commands
            .as_ref()
            .ok_or("Streamende bereits abgeschlossen")?
            .try_send(Message::Control(action.into()))
            .map_err(|_| "Streamende-Steuerung ausgelastet; erneut versuchen".to_string())?;
        state.snapshot.pending_action = Some(action.into());
        let _ = self.changes.send(state.snapshot.clone());
        Ok(())
    }
    pub async fn observe_twitch(&self, event: TwitchEvent) {
        if event.event_type != "channel.raid" {
            return;
        }
        let state = self.state.lock().await;
        if state
            .expected
            .as_ref()
            .is_some_and(|proof| proof.matches(&event))
        {
            // Keep the latest valid proof independently of the bounded control queue.
            // Incoming/unrelated events cannot overwrite it, even during a slow POST.
            if let Some(confirmations) = &state.confirmations {
                confirmations.send_replace(Some(event));
            }
        }
    }
    pub async fn observe_obs_stopped(&self) {
        let state = self.state.lock().await;
        if state.snapshot.active
            && !matches!(state.snapshot.phase.as_str(), "stopping" | "finalizing")
        {
            if let Some(commands) = &state.commands {
                let _ = commands.try_send(Message::ExternalStop);
            }
        }
    }
    /// Host calls this only after a successful Twitch cancellation or explicit manual
    /// acknowledgement. It never completes an active raid/stream-end flow.
    pub async fn resolve_pending_raid(&self) -> Result<(), String> {
        let mut state = self.state.lock().await;
        if state.snapshot.active {
            return Err("Aktiven Raid im Streamende-Assistenten steuern".into());
        }
        state.snapshot.raid_pending = false;
        let _ = self.changes.send(state.snapshot.clone());
        Ok(())
    }
    async fn publish(&self, snapshot: &StreamEndSnapshot, expected: Option<RaidProof>) {
        let mut state = self.state.lock().await;
        if state.snapshot.run_id != snapshot.run_id {
            return;
        }
        state.snapshot = snapshot.clone();
        state.expected = expected;
        if !snapshot.active {
            state.commands = None;
            state.confirmations = None;
            state.expected = None;
        }
        let _ = self.changes.send(snapshot.clone());
    }
}
pub fn retry_delay(attempt: u32) -> u64 {
    match attempt {
        0 | 1 => 5,
        2 => 8,
        3 => 12,
        _ => 15,
    }
}
fn known_refusal(error: &str) -> bool {
    let error = error.to_lowercase();
    [
        "eigenen kanal",
        "eigene kanal",
        "nicht gefunden",
        "nicht verbunden",
        "nicht konfiguriert",
    ]
    .iter()
    .any(|needle| error.contains(needle))
}
fn http_status(error: &str) -> Option<u16> {
    error
        .strip_prefix("Twitch API ")
        .and_then(|tail| tail.split_whitespace().next())
        .and_then(|status| status.trim_end_matches(':').parse().ok())
}
fn permanent(error: &str) -> bool {
    known_refusal(error) || matches!(http_status(error), Some(401 | 403))
}
fn ambiguous(error: &str) -> bool {
    if error.to_lowercase().contains("raid-ausgang unklar") {
        return true;
    }
    // A known client-side refusal did not perform a POST. For HTTP replies only a
    // definite 4xx failure is safe to retry/finish; transport and 5xx results may
    // have arrived after Twitch accepted the mutation.
    if permanent(error) || error == "Das Raid-Ziel ist offline." {
        return false;
    }
    !matches!(http_status(error), Some(400..=499))
}
struct Worker {
    runtime: Arc<StreamEndRuntime>,
    io: Arc<dyn StreamEndIo>,
    plan: StreamEndPlan,
    snapshot: StreamEndSnapshot,
    rx: mpsc::Receiver<Message>,
    confirmation_rx: watch::Receiver<Option<TwitchEvent>>,
    abort: bool,
    skip_raid: bool,
    cancel_raid: bool,
    raid_now: bool,
    skip_end: bool,
    start_now: bool,
    external_stop: bool,
    raid_confirmed: bool,
    raid_requested_at: Option<DateTime<Utc>>,
    target: Option<RaidIdentity>,
    raid_started: Option<Instant>,
}
enum RaidOutcome {
    Confirmed,
    Skipped,
    Retry,
    Aborted,
}
impl Worker {
    fn proof(&self) -> Option<RaidProof> {
        if !self.snapshot.raid_pending {
            return None;
        }
        let target = self.target.as_ref()?;
        Some(RaidProof {
            from_id: self.plan.broadcaster_id.clone(),
            from_login: self.plan.broadcaster_login.clone(),
            to_id: target.id.clone(),
            to_login: target.login.clone(),
            requested_at: self.raid_requested_at?,
        })
    }
    async fn publish(&mut self) {
        self.runtime.publish(&self.snapshot, self.proof()).await;
    }
    async fn phase(&mut self, phase: &str, status: &str) {
        if self.snapshot.active {
            self.drain();
        }
        self.snapshot.phase = phase.into();
        self.snapshot.status = status.into();
        self.snapshot.can_raid_now = false;
        self.publish().await;
    }
    fn consume(&mut self, message: Message) {
        match message {
            Message::Control(action) => {
                self.snapshot.pending_action = Some(action.clone());
                match action.as_str() {
                    "abort" => self.abort = true,
                    "skip_raid" => self.skip_raid = true,
                    "cancel_raid" => self.cancel_raid = true,
                    "raid_now" => self.raid_now = true,
                    "skip_end" => self.skip_end = true,
                    "start_now" => self.start_now = true,
                    _ => {}
                }
            }
            Message::Event(event) => {
                self.raid_confirmed |= self.proof().is_some_and(|proof| proof.matches(&event));
            }
            Message::ExternalStop => {
                self.external_stop = true;
                self.abort = true;
            }
        }
    }
    fn drain(&mut self) {
        while let Ok(message) = self.rx.try_recv() {
            self.consume(message);
        }
        if self.confirmation_rx.has_changed().unwrap_or(false) {
            let event = self.confirmation_rx.borrow_and_update().clone();
            if let Some(event) = event {
                self.consume(Message::Event(event));
            }
        }
    }
    async fn delay(&mut self, until: Instant) {
        loop {
            self.drain();
            if self.abort
                || self.skip_raid
                || self.cancel_raid
                || self.raid_now
                || self.skip_end
                || self.start_now
                || self.raid_confirmed
                || Instant::now() >= until
            {
                break;
            }
            tokio::select! {
                message=self.rx.recv()=>if let Some(message)=message{self.consume(message);},
                changed=self.confirmation_rx.changed()=>if changed.is_ok() {
                    let event=self.confirmation_rx.borrow_and_update().clone();
                    if let Some(event)=event {self.consume(Message::Event(event));}
                },
                _=tokio::time::sleep_until(until)=>break
            }
        }
    }
    async fn countdown(&mut self, seconds: u32, scheduled: bool) -> bool {
        let deadline = Instant::now() + Duration::from_secs(seconds.into());
        self.snapshot.total_seconds = seconds;
        loop {
            self.drain();
            if self.abort {
                return false;
            }
            if Instant::now() >= deadline
                || (scheduled && self.start_now)
                || (!scheduled && self.skip_end)
            {
                break;
            }
            self.snapshot.remaining_seconds = deadline
                .saturating_duration_since(Instant::now())
                .as_millis()
                .div_ceil(1000)
                .min(seconds.into()) as u32;
            self.publish().await;
            self.delay(deadline.min(Instant::now() + Duration::from_secs(1)))
                .await;
        }
        self.start_now = false;
        self.skip_end = false;
        self.snapshot.pending_action = None;
        self.snapshot.remaining_seconds = 0;
        true
    }
    async fn warning_operation(&mut self, operation: StreamEndOperation, label: &str) {
        match self.io.execute(operation).await {
            Err(error) => self.snapshot.warnings.push(format!("{label}: {error}")),
            Ok(StreamEndReply::Warnings(warnings)) => self.snapshot.warnings.extend(warnings),
            _ => return,
        }
        self.publish().await;
    }
    async fn abort_cleanup(&mut self) {
        self.drain();
        if self.raid_confirmed {
            self.warning_operation(
                StreamEndOperation::AcknowledgeRaid,
                "Raid-Bestätigung konnte nicht übernommen werden",
            )
            .await;
            self.snapshot.raid_pending = false;
        } else if self.snapshot.raid_pending {
            match self.io.execute(StreamEndOperation::CancelRaid).await {
                Ok(_) => self.snapshot.raid_pending = false,
                Err(error) => self.snapshot.warnings.push(format!(
                    "Raid konnte nicht abgebrochen werden; in Twitch prüfen: {error}"
                )),
            }
        }
        self.finish(
            "aborted",
            if self.external_stop {
                "Stream wurde außerhalb des Assistenten beendet"
            } else {
                "Streamende abgebrochen; Stream läuft weiter"
            },
            None,
        )
        .await;
    }
    async fn finish(&mut self, phase: &str, status: &str, error: Option<String>) {
        self.snapshot.active = false;
        self.snapshot.pending_action = None;
        self.snapshot.can_raid_now = false;
        self.snapshot.remaining_seconds = 0;
        self.snapshot.error = error;
        self.phase(phase, status).await;
    }
    async fn run(mut self, delay_seconds: u32) {
        if delay_seconds > 0 {
            self.phase("scheduled", "Streamende geplant").await;
            if !self.countdown(delay_seconds, true).await {
                self.abort_cleanup().await;
                return;
            }
        }
        self.drain();
        if self.abort {
            self.abort_cleanup().await;
            return;
        }
        if self.plan.preferences.mode != "Immediate" {
            self.phase("preparing", "Endszene wird vorbereitet").await;
            if self.abort {
                self.abort_cleanup().await;
                return;
            }
            if !self.plan.end_scene.trim().is_empty() {
                if let Err(error) = self
                    .io
                    .execute(StreamEndOperation::SetScene {
                        scene: self.plan.end_scene.clone(),
                    })
                    .await
                {
                    self.finish(
                        "error",
                        "Endszene konnte nicht aktiviert werden",
                        Some(error),
                    )
                    .await;
                    return;
                }
            }
            self.drain();
            if self.abort {
                self.abort_cleanup().await;
                return;
            }
            if self.plan.play_end_music {
                self.warning_operation(
                    StreamEndOperation::PlayEndMusic,
                    "Endmusik konnte nicht gestartet werden",
                )
                .await;
            }
            self.drain();
            if self.abort {
                self.abort_cleanup().await;
                return;
            }
            let wants_raid = self.plan.preferences.mode == "EndSceneRaidThenStop"
                && !self.snapshot.target_login.is_empty();
            if wants_raid {
                match self.raid_flow().await {
                    RaidOutcome::Aborted => {
                        self.abort_cleanup().await;
                        return;
                    }
                    RaidOutcome::Confirmed => {
                        self.warning_operation(
                            StreamEndOperation::AcknowledgeRaid,
                            "Raid-Bestätigung konnte nicht übernommen werden",
                        )
                        .await;
                        self.snapshot.raid_pending = false;
                        if self.plan.preferences.stop_music_after_raid {
                            self.warning_operation(
                                StreamEndOperation::PauseMusic,
                                "Musik konnte nicht pausiert werden",
                            )
                            .await;
                        }
                        if !self.plan.preferences.stop_stream_after_raid {
                            self.finish("completed", "Raid bestätigt; Stream läuft weiter", None)
                                .await;
                            return;
                        }
                    }
                    RaidOutcome::Skipped => {}
                    RaidOutcome::Retry => unreachable!(),
                }
            } else {
                self.phase("end_scene", "Endszene läuft").await;
                if !self
                    .countdown(self.plan.preferences.end_scene_seconds, false)
                    .await
                {
                    self.abort_cleanup().await;
                    return;
                }
            }
        }
        self.drain();
        if self.abort {
            self.abort_cleanup().await;
            return;
        }
        self.phase("stopping", "OBS-Stream wird beendet").await;
        if self.abort {
            self.abort_cleanup().await;
            return;
        }
        let mut last_error = String::new();
        let mut stopped = false;
        for attempt in 0..3 {
            match self.io.execute(StreamEndOperation::StopStream).await {
                Ok(_) => {
                    stopped = true;
                    break;
                }
                Err(error) => last_error = error,
            }
            if attempt < 2 {
                tokio::time::sleep(Duration::from_secs(1)).await;
            }
        }
        if !stopped {
            self.finish(
                "error",
                "Stream konnte nicht beendet werden",
                Some(last_error),
            )
            .await;
            return;
        }
        self.phase(
            "finalizing",
            "Stream beendet; Bedienpult wird zurückgesetzt",
        )
        .await;
        if !self.plan.start_scene.trim().is_empty() {
            self.warning_operation(
                StreamEndOperation::SetScene {
                    scene: self.plan.start_scene.clone(),
                },
                "Startszene konnte nicht aktiviert werden",
            )
            .await;
        }
        if self.plan.pause_music_on_stream_end {
            self.warning_operation(
                StreamEndOperation::PauseMusic,
                "Musik konnte nicht pausiert werden",
            )
            .await;
        }
        self.finish("completed", "Stream beendet", None).await;
    }
    async fn raid_flow(&mut self) -> RaidOutcome {
        let deadline = Instant::now()
            + Duration::from_secs(self.plan.preferences.raid_start_timeout_seconds.into());
        loop {
            self.drain();
            if self.abort {
                return RaidOutcome::Aborted;
            }
            if self.skip_raid {
                return RaidOutcome::Skipped;
            }
            if Instant::now() >= deadline {
                self.snapshot
                    .warnings
                    .push("Raid-Start-Timeout; Stream wird ohne Raid beendet".into());
                return RaidOutcome::Skipped;
            }
            self.snapshot.attempt += 1;
            self.phase("raid_probe", "Raid-Ziel wird geprüft").await;
            let result = self
                .io
                .execute(StreamEndOperation::ProbeRaid {
                    login: self.snapshot.target_login.clone(),
                })
                .await;
            self.drain();
            if self.abort {
                return RaidOutcome::Aborted;
            }
            if self.skip_raid {
                return RaidOutcome::Skipped;
            }
            // A slow read must not start a new raid after its start budget expired.
            if Instant::now() >= deadline {
                continue;
            }
            let retry = match result {
                Ok(StreamEndReply::Target(Some(target))) if target.online => {
                    if target.id == self.plan.broadcaster_id
                        || target
                            .login
                            .eq_ignore_ascii_case(&self.plan.broadcaster_login)
                    {
                        self.snapshot
                            .warnings
                            .push("Raid zum eigenen Kanal wurde verworfen".into());
                        return RaidOutcome::Skipped;
                    }
                    if target.id.trim().is_empty()
                        || !target
                            .login
                            .eq_ignore_ascii_case(&self.snapshot.target_login)
                    {
                        self.snapshot
                            .warnings
                            .push("Raid-Zielprüfung lieferte kein eindeutiges Ziel".into());
                        return RaidOutcome::Skipped;
                    }
                    self.snapshot.target_display_name = target.display_name.clone();
                    self.target = Some(target);
                    self.phase("raid_starting", "Raid wird bei Twitch angefragt")
                        .await;
                    if self.abort {
                        return RaidOutcome::Aborted;
                    }
                    if self.skip_raid {
                        return RaidOutcome::Skipped;
                    }
                    self.raid_requested_at = Some(Utc::now());
                    // Mark in-flight before awaiting I/O so queued EventSub proofs are not discarded.
                    self.snapshot.raid_pending = true;
                    self.publish().await;
                    let result = self
                        .io
                        .execute(StreamEndOperation::StartRaid {
                            login: self.snapshot.target_login.clone(),
                        })
                        .await;
                    match result {
                        Ok(reply) => {
                            if let StreamEndReply::Warnings(warnings) = reply {
                                self.snapshot.warnings.extend(warnings);
                            }
                            self.raid_started = Some(Instant::now());
                            self.phase(
                                "raid_countdown",
                                "Raid-Countdown; Bestätigung von Twitch wird abgewartet",
                            )
                            .await;
                            match self.wait_raid(false).await {
                                RaidOutcome::Retry => 5,
                                outcome => return outcome,
                            }
                        }
                        Err(error) if ambiguous(&error) => {
                            self.snapshot.error = Some(error);
                            self.phase(
                                "raid_uncertain",
                                "Raid-Ausgang unklar; bei Twitch prüfen oder abbrechen",
                            )
                            .await;
                            match self.wait_raid(true).await {
                                RaidOutcome::Retry => 5,
                                outcome => return outcome,
                            }
                        }
                        Err(error) => {
                            self.drain();
                            if self.raid_confirmed {
                                return if self.abort {
                                    RaidOutcome::Aborted
                                } else {
                                    RaidOutcome::Confirmed
                                };
                            }
                            self.snapshot.raid_pending = false;
                            self.raid_requested_at = None;
                            if self.abort {
                                return RaidOutcome::Aborted;
                            }
                            if self.skip_raid {
                                return RaidOutcome::Skipped;
                            }
                            self.snapshot.error = Some(error.clone());
                            if permanent(&error) {
                                self.snapshot
                                    .warnings
                                    .push(format!("Raid nicht gestartet: {error}"));
                                return RaidOutcome::Skipped;
                            }
                            retry_delay(self.snapshot.attempt)
                        }
                    }
                }
                Ok(StreamEndReply::Target(_)) => {
                    self.snapshot.status =
                        "Raid-Ziel nicht gefunden oder offline; wird erneut geprüft".into();
                    5
                }
                Ok(StreamEndReply::Done | StreamEndReply::Warnings(_)) => {
                    self.snapshot
                        .warnings
                        .push("Raid-Zielprüfung lieferte ungültige Antwort".into());
                    return RaidOutcome::Skipped;
                }
                Err(error) => {
                    self.snapshot.error = Some(error.clone());
                    if permanent(&error) {
                        self.snapshot
                            .warnings
                            .push(format!("Raid-Ziel konnte nicht geprüft werden: {error}"));
                        return RaidOutcome::Skipped;
                    }
                    retry_delay(self.snapshot.attempt)
                }
            };
            self.phase("raid_retry", "Raid wird erneut geprüft").await;
            self.delay(deadline.min(Instant::now() + Duration::from_secs(retry)))
                .await;
        }
    }
    async fn wait_raid(&mut self, uncertain: bool) -> RaidOutcome {
        let seconds = self.plan.preferences.raid_countdown_seconds;
        self.snapshot.total_seconds = if uncertain { 0 } else { seconds };
        loop {
            self.drain();
            if self.abort {
                return RaidOutcome::Aborted;
            }
            if self.raid_confirmed {
                return RaidOutcome::Confirmed;
            }
            if self.cancel_raid || self.skip_raid {
                let skip = self.skip_raid;
                self.cancel_raid = false;
                self.skip_raid = false;
                match self.io.execute(StreamEndOperation::CancelRaid).await {
                    Ok(_) => {
                        // A raid may complete while cancellation is in flight. Keep an actual
                        // completion proof; a successful cancel response is not that proof.
                        self.drain();
                        if self.raid_confirmed {
                            return if self.abort {
                                RaidOutcome::Aborted
                            } else {
                                RaidOutcome::Confirmed
                            };
                        }
                        self.snapshot.raid_pending = false;
                        self.raid_requested_at = None;
                        self.raid_started = None;
                        self.snapshot.error = None;
                        self.snapshot.pending_action = None;
                        self.raid_now = false;
                        if self.abort {
                            return RaidOutcome::Aborted;
                        }
                        return if skip {
                            RaidOutcome::Skipped
                        } else {
                            RaidOutcome::Retry
                        };
                    }
                    Err(error) => {
                        self.snapshot.pending_action = None;
                        self.snapshot.error =
                            Some(format!("Raid konnte nicht abgebrochen werden: {error}"));
                        self.publish().await;
                    }
                }
            }
            if self.raid_now {
                self.raid_now = false;
                if let Err(error) = self
                    .io
                    .execute(StreamEndOperation::RaidNow {
                        login: self.snapshot.target_login.clone(),
                    })
                    .await
                {
                    self.snapshot.error =
                        Some(format!("Raid-Befehl konnte nicht gesendet werden: {error}"));
                }
                self.snapshot.pending_action = None;
                // A sent chat command never replaces outgoing EventSub confirmation.
            }
            let elapsed = self.raid_started.map_or(0, |at| {
                Instant::now().saturating_duration_since(at).as_secs()
            });
            self.snapshot.remaining_seconds = if uncertain {
                0
            } else {
                seconds.saturating_sub(elapsed.min(u32::MAX.into()) as u32)
            };
            self.snapshot.can_raid_now = !uncertain && elapsed >= 10;
            if !uncertain && self.snapshot.remaining_seconds == 0 {
                self.snapshot.phase = "awaiting_raid".into();
                self.snapshot.status =
                    "Raid bereit; bei Twitch ausführen. Bestätigung wird abgewartet".into();
            }
            self.publish().await;
            self.delay(Instant::now() + Duration::from_secs(1)).await;
        }
    }
}

#[cfg(test)]
#[path = "stream_end_tests.rs"]
mod tests;
