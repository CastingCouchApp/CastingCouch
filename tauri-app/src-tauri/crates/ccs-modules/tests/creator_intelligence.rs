use ccs_modules::creator_intelligence::{analyze, CreatorIntelligenceRuntime, JournalEvent};
use ccs_modules::stream_history::StreamHistoryRuntime;
use ccs_overlay_server::RealtimeHub;
use chrono::{DateTime, Duration, FixedOffset, TimeZone, Utc};
use serde_json::{json, Value};
use std::{fs, sync::Arc};

fn assert_equivalent(actual: &Value, expected: &Value, path: &str) {
    match (actual, expected) {
        (Value::Number(a), Value::Number(b)) => assert!(
            (a.as_f64().unwrap() - b.as_f64().unwrap()).abs() < 1e-8,
            "{path}: {a} != {b}"
        ),
        (Value::Array(a), Value::Array(b)) => {
            assert_eq!(a.len(), b.len(), "{path}");
            for (index, (a, b)) in a.iter().zip(b).enumerate() {
                assert_equivalent(a, b, &format!("{path}[{index}]"));
            }
        }
        (Value::Object(a), Value::Object(b)) => {
            for (key, b) in b {
                assert_equivalent(
                    a.get(key).unwrap_or(&Value::Null),
                    b,
                    &format!("{path}.{key}"),
                );
            }
        }
        (Value::String(a), Value::String(b))
            if path.ends_with("StartedAt") || path.ends_with("EndedAt") =>
        {
            assert_eq!(
                DateTime::parse_from_rfc3339(a).unwrap(),
                DateTime::parse_from_rfc3339(b).unwrap(),
                "{path}"
            );
        }
        _ => assert_eq!(actual, expected, "{path}"),
    }
}

fn event(at: DateTime<Utc>, kind: &str, payload: Value) -> JournalEvent {
    JournalEvent {
        timestamp_utc: at,
        session_id: "session".into(),
        kind: kind.into(),
        payload: Some(payload),
    }
}
fn now() -> DateTime<Utc> {
    Utc.with_ymd_and_hms(2026, 10, 3, 10, 0, 0).unwrap()
}
fn analyze_test(events: &[JournalEvent]) -> ccs_modules::creator_intelligence::Analysis {
    analyze(events, 30, now(), |at| at.fixed_offset())
}
#[test]
fn sparse_and_incomplete_sessions_are_finite_and_excluded_from_trends() {
    let empty = analyze_test(&[]);
    assert!(empty.latest.is_none());
    assert_eq!(empty.dashboard.session_count, 0);
    let events = vec![
        event(now(), "session.started", json!({})),
        event(now(), "twitch.chat.message", json!({})),
    ];
    let result = analyze_test(&events);
    assert_eq!(result.latest.unwrap().chat_messages_per_hour, 60.);
    assert_eq!(result.dashboard.session_count, 0);
}

#[test]
fn fractional_timespan_and_rates_keep_csharp_tick_precision() {
    let at = now() - Duration::hours(2);
    let end = at + Duration::hours(1) + Duration::nanoseconds(123456700);
    let result = analyze_test(&[
        event(at, "session.started", json!({})),
        event(at, "twitch.chat.message", json!({})),
        event(end, "session.ended", json!({})),
    ]);
    let session = result.latest.unwrap();
    assert_eq!(session.duration, "01:00:00.1234567");
    assert!((session.chat_messages_per_hour - 3600. / 3600.1234567).abs() < 1e-14);
}
#[test]
fn retention_clamps_rounding_and_local_day_boundaries_match_reference() {
    let at = now() - Duration::hours(11);
    let events = vec![
        event(at, "session.started", json!({})),
        event(at, "twitch.viewer.sample", json!({"viewers":10})),
        event(
            at + Duration::minutes(10),
            "twitch.viewer.sample",
            json!({"viewers":100}),
        ),
        event(at + Duration::hours(1), "session.ended", json!({})),
    ];
    let result = analyze(&events, 30, now(), |at| {
        at.with_timezone(&FixedOffset::east_opt(7200).unwrap())
    });
    assert_eq!(result.latest.as_ref().unwrap().retention_percent, 200.);
    assert_eq!(result.latest.as_ref().unwrap().creator_score, 71);
    assert_eq!(result.dashboard.best_start_hour, 1);
    assert_eq!(result.dashboard.best_day, 6); // Saturday locally, Friday UTC.
                                              // No viewers gives 100% retention, score 35. Exactly one sample gives
                                              // score 35.5 rounded to 36 using .NET's midpoint-to-even convention.
    let midpoint = vec![
        event(at, "session.started", json!({})),
        event(at, "twitch.viewer.sample", json!({"viewers":10})),
        event(at + Duration::hours(1), "session.ended", json!({})),
    ];
    assert_eq!(analyze_test(&midpoint).latest.unwrap().creator_score, 36);
}
#[test]
fn native_music_title_is_analyzed_and_windows_reject_samples_more_than_twelve_minutes_away() {
    let at = now() - Duration::hours(1);
    let events = vec![
        event(at, "session.started", json!({})),
        event(at, "twitch.viewer.sample", json!({"viewers":10})),
        event(
            at,
            "spotify.track.changed",
            json!({"title":"Native song","artist":"Artist"}),
        ),
        event(
            at + Duration::minutes(18),
            "twitch.viewer.sample",
            json!({"viewers":20}),
        ),
        event(now(), "session.ended", json!({})),
    ];
    let result = analyze_test(&events);
    assert_eq!(result.content.tracks[0].name, "Native song – Artist");
    assert!(result.correlation.correlations.is_empty()); // +5 target is 13 minutes from sample.
    let mut boundary = events;
    boundary[3].timestamp_utc -= Duration::minutes(1);
    let result = analyze_test(&boundary);
    assert_eq!(
        result.correlation.correlations[0].viewer_delta5_minutes,
        10.
    );
    boundary[3].timestamp_utc += Duration::nanoseconds(100);
    assert!(analyze_test(&boundary).correlation.correlations.is_empty());
}

fn runtime(root: &std::path::Path) -> (Arc<StreamHistoryRuntime>, CreatorIntelligenceRuntime) {
    let history = Arc::new(StreamHistoryRuntime::new(
        root.into(),
        Arc::new(RealtimeHub::new()),
    ));
    (
        history.clone(),
        CreatorIntelligenceRuntime::new(root.into(), history),
    )
}
fn write_journal(root: &std::path::Path, events: &[JournalEvent]) {
    let path = root.join("CreatorIntelligence/2026-10/events.jsonl");
    fs::create_dir_all(path.parent().unwrap()).unwrap();
    fs::write(
        path,
        events
            .iter()
            .map(|e| serde_json::to_string(e).unwrap() + "\n")
            .collect::<String>(),
    )
    .unwrap();
}
#[test]
fn runtime_reads_the_complete_journal_and_keeps_damaged_rows_and_unknown_fields() {
    let root = tempfile::tempdir().unwrap();
    let fixture: Value =
        serde_json::from_str(include_str!("fixtures/creator-intelligence.json")).unwrap();
    let events: Vec<JournalEvent> = serde_json::from_value(fixture["events"].clone()).unwrap();
    write_journal(root.path(), &events);
    let file = root.path().join("CreatorIntelligence/2026-10/events.jsonl");
    let bytes = fs::read_to_string(&file).unwrap() + "damaged\n";
    fs::write(&file, &bytes).unwrap();
    let (_, runtime) = runtime(root.path());
    let at = DateTime::parse_from_rfc3339(fixture["now"].as_str().unwrap())
        .unwrap()
        .with_timezone(&Utc);
    let snapshot = runtime.snapshot_at(30, at).unwrap();
    assert_eq!(snapshot.analysis.dashboard.session_count, 12);
    assert!(!snapshot.warnings.is_empty());
    assert_eq!(fs::read_to_string(file).unwrap(), bytes);
}
#[test]
fn notes_are_active_session_only_serialized_idempotent_and_survive_restart() {
    let root = tempfile::tempdir().unwrap();
    let (history, runtime) = runtime(root.path());
    assert!(runtime.note_at("note", "request-1", now()).is_err());
    history.observe(&json!({"stream":{"available":true,"isLive":true,"elapsedSeconds":0},"obs":{"currentScene":"Main"}}), &json!({}), &json!({}), now()).unwrap();
    runtime
        .note_at("  Interview  ", "request-1", now())
        .unwrap();
    runtime
        .note_at("  Interview  ", "request-1", now())
        .unwrap();
    assert!(runtime.note_at(" ", "request-2", now()).is_err());
    let snapshot = history.snapshot(None).unwrap();
    let notes: Vec<_> = snapshot
        .events
        .iter()
        .filter(|e| e["Type"] == "session.note")
        .collect();
    assert_eq!(notes.len(), 1);
    assert_eq!(
        notes[0]["Payload"],
        json!({"note":"Interview","scene":"Main","viewers":0})
    );
    let restarted = runtime::new_runtime(root.path());
    restarted
        .0
        .record_note("Interview", "request-1", now())
        .unwrap();
    assert_eq!(
        restarted
            .0
            .snapshot(None)
            .unwrap()
            .events
            .iter()
            .filter(|e| e["Type"] == "session.note")
            .count(),
        1
    );
}
mod runtime {
    pub fn new_runtime(
        root: &std::path::Path,
    ) -> (
        std::sync::Arc<ccs_modules::stream_history::StreamHistoryRuntime>,
        ccs_modules::creator_intelligence::CreatorIntelligenceRuntime,
    ) {
        super::runtime(root)
    }
}
#[test]
fn weekly_report_escapes_names_limits_to_seven_days_and_never_overwrites_reports() {
    let root = tempfile::tempdir().unwrap();
    let at = now() - Duration::hours(1);
    write_journal(
        root.path(),
        &[
            event(
                at,
                "session.started",
                json!({"title":"<script>","category":"<svg onload=alert(1)>"}),
            ),
            event(at, "obs.scene.changed", json!({"scene":"<img src=x>"})),
            event(at, "twitch.viewer.sample", json!({"viewers":10})),
            event(now(), "session.ended", json!({})),
        ],
    );
    let (_, runtime) = runtime(root.path());
    let first = runtime.weekly_report_at(now()).unwrap();
    let second = runtime.weekly_report_at(now()).unwrap();
    assert_ne!(first, second);
    let html = fs::read_to_string(first).unwrap();
    assert!(html.contains("&lt;svg onload=alert(1)&gt;"));
    assert!(!html.contains("<img src=x>"));
    assert!(html.contains("&lt;img src=x&gt;"));
    let mut old = vec![
        event(
            now() - Duration::days(20),
            "session.started",
            json!({"category":"Old category"}),
        ),
        event(now() - Duration::days(19), "session.ended", json!({})),
    ];
    for row in &mut old {
        row.session_id = "old".into();
    }
    let old_file = root.path().join("CreatorIntelligence/older/events.jsonl");
    fs::create_dir_all(old_file.parent().unwrap()).unwrap();
    fs::write(
        old_file,
        old.iter()
            .map(|e| serde_json::to_string(e).unwrap() + "\n")
            .collect::<String>(),
    )
    .unwrap();
    let html = fs::read_to_string(runtime.weekly_report_at(now()).unwrap()).unwrap();
    assert!(html.contains("Streams: 1"));
    assert!(!html.contains("Old category"));
}
#[test]
fn corrupt_action_plan_or_experiments_remain_protected_and_analysis_still_available() {
    let root = tempfile::tempdir().unwrap();
    let (_, runtime) = runtime(root.path());
    fs::create_dir_all(root.path().join("CreatorIntelligence")).unwrap();
    for name in ["action-plan.json", "experiments.json"] {
        let file = root.path().join("CreatorIntelligence").join(name);
        fs::write(&file, "{broken").unwrap();
        let snapshot = runtime.snapshot_at(30, now()).unwrap();
        assert!(!snapshot.warnings.is_empty());
        assert!(runtime.complete_action_at("any", now()).is_err());
        assert!(runtime.start_experiment_at("any", now()).is_err());
        assert_eq!(fs::read_to_string(&file).unwrap(), "{broken");
        fs::remove_file(file).unwrap();
    }
}
#[test]
fn actions_and_experiments_preserve_csharp_records_extensions_and_serialized_updates() {
    let root = tempfile::tempdir().unwrap();
    let at = now() - Duration::hours(1);
    write_journal(
        root.path(),
        &[
            event(at, "session.started", json!({})),
            event(at, "twitch.viewer.sample", json!({"viewers":10})),
            event(now(), "session.ended", json!({})),
        ],
    );
    fs::write(root.path().join("CreatorIntelligence/action-plan.json"),json!([{"Id":"existing","Title":"Chat goal","Metric":"engagement","Baseline":0,"Target":15,"Priority":1,"Status":"Offen","CreatedAt":at,"CompletedAt":null,"CurrentValue":null,"Future":{"preserve":[1,2]}}]).to_string()).unwrap();
    let (_, runtime) = runtime(root.path());
    let first = runtime.snapshot_at(30, now()).unwrap();
    assert!(first.actions.as_ref().unwrap().items.len() >= 2);
    runtime.start_experiment_at("existing", now()).unwrap();
    runtime.start_experiment_at("existing", now()).unwrap();
    runtime.complete_action_at("existing", now()).unwrap();
    let after = runtime.snapshot_at(30, now()).unwrap();
    assert_eq!(after.experiments.unwrap().rows.len(), 1);
    let items: Value = serde_json::from_slice(
        &fs::read(root.path().join("CreatorIntelligence/action-plan.json")).unwrap(),
    )
    .unwrap();
    let row = items
        .as_array()
        .unwrap()
        .iter()
        .find(|r| r["Id"] == "existing")
        .unwrap();
    assert_eq!(row["Status"], "Erledigt");
    assert_eq!(row["Future"], json!({"preserve":[1,2]}));
    assert!(runtime.complete_action_at("missing", now()).is_err());
}

#[test]
fn csharp_reference_matches_action_plan_effectiveness_and_completed_experiments() {
    let root = tempfile::tempdir().unwrap();
    let fixture: Value =
        serde_json::from_str(include_str!("fixtures/creator-intelligence.json")).unwrap();
    let events: Vec<JournalEvent> = serde_json::from_value(fixture["events"].clone()).unwrap();
    write_journal(root.path(), &events);
    for (file, key) in [
        ("action-plan.json", "actionsInput"),
        ("experiments.json", "experimentsInput"),
    ] {
        fs::write(
            root.path().join("CreatorIntelligence").join(file),
            fixture[key].to_string(),
        )
        .unwrap();
    }
    let (_, runtime) = runtime(root.path());
    let at = DateTime::parse_from_rfc3339(fixture["now"].as_str().unwrap())
        .unwrap()
        .with_timezone(&Utc);
    let snapshot = serde_json::to_value(runtime.snapshot_at(30, at).unwrap()).unwrap();
    // Generated IDs and mutation timestamps are intentionally nondeterministic in C#.
    // Separate persistence tests verify their presence, stability and restart behavior.
    fn semantic(value: &Value) -> Value {
        match value {
            Value::Object(v) => Value::Object(
                v.iter()
                    .filter(|(key, _)| !matches!(key.as_str(), "Id" | "CreatedAt" | "CompletedAt"))
                    .map(|(k, v)| (k.clone(), semantic(v)))
                    .collect(),
            ),
            Value::Array(v) => Value::Array(v.iter().map(semantic).collect()),
            _ => value.clone(),
        }
    }
    for key in ["actions", "effectiveness", "experiments"] {
        assert_equivalent(&semantic(&snapshot[key]), &semantic(&fixture[key]), key);
    }
}

#[test]
fn failed_note_write_retries_without_duplicate_rows_and_notifies_clients() {
    let root = tempfile::tempdir().unwrap();
    let (history, runtime) = runtime(root.path());
    history.observe_stream_event(true, now()).unwrap();
    let mut changes = history.subscribe_changes();
    let month = now()
        .with_timezone(&chrono::Local)
        .format("%Y-%m")
        .to_string();
    let file = root
        .path()
        .join("CreatorIntelligence")
        .join(month)
        .join("events.jsonl");
    let backup = file.with_extension("original");
    fs::rename(&file, &backup).unwrap();
    fs::create_dir(&file).unwrap();
    assert!(runtime.note_at("Pending", "once", now()).is_err());
    assert!(changes.try_recv().is_ok());
    fs::remove_dir(&file).unwrap();
    fs::rename(backup, &file).unwrap();
    history.retry().unwrap();
    runtime.note_at("Pending", "once", now()).unwrap();
    assert_eq!(
        history
            .snapshot(None)
            .unwrap()
            .events
            .iter()
            .filter(|e| e["Type"] == "session.note")
            .count(),
        1
    );
}

#[test]
fn plan_write_failure_is_visible_and_recoverable_without_losing_existing_analysis() {
    let root = tempfile::tempdir().unwrap();
    let (_, runtime) = runtime(root.path());
    let blocked = root.path().join("CreatorIntelligence/action-plan.json");
    fs::create_dir_all(&blocked).unwrap();
    let snapshot = runtime.snapshot_at(30, now()).unwrap();
    assert!(snapshot.actions.is_none());
    assert!(!snapshot.warnings.is_empty());
    assert_eq!(snapshot.analysis.dashboard.session_count, 0);
    assert!(runtime.complete_action_at("missing", now()).is_err());
    fs::remove_dir(&blocked).unwrap();
    assert!(runtime.snapshot_at(30, now()).unwrap().actions.is_some());
}

#[test]
fn concurrent_action_updates_are_serialized_and_preserve_experiment_extensions() {
    let root = tempfile::tempdir().unwrap();
    let (_, runtime) = runtime(root.path());
    let runtime = Arc::new(runtime);
    let plan = runtime.snapshot_at(30, now()).unwrap().actions.unwrap();
    std::thread::scope(|scope| {
        for row in &plan.items {
            let runtime = &runtime;
            scope.spawn(move || runtime.complete_action_at(&row.id, now()).unwrap());
        }
    });
    assert!(runtime
        .snapshot_at(30, now())
        .unwrap()
        .actions
        .unwrap()
        .items
        .iter()
        .all(|r| r.status == "Erledigt"));
    let file = root.path().join("CreatorIntelligence/experiments.json");
    fs::write(&file,json!([{"Id":"old","ActionId":"original","Title":"Test","Metric":"score","Baseline":25,"TargetSessions":3,"Status":"Aktiv","StartedAt":now(),"CompletedAt":null,"Future":{"preserve":true}}]).to_string()).unwrap();
    let snapshot = runtime.snapshot_at(30, now()).unwrap();
    assert_eq!(snapshot.experiments.unwrap().active_count, 1);
    let raw: Value = serde_json::from_slice(&fs::read(&file).unwrap()).unwrap();
    assert_eq!(raw[0]["Future"], json!({"preserve":true}));
    fs::write(
        &file,
        raw.to_string()
            .replace("\"TargetSessions\":3", "\"TargetSessions\":0"),
    )
    .unwrap();
    let invalid = fs::read_to_string(&file).unwrap();
    assert!(runtime
        .snapshot_at(30, now())
        .unwrap()
        .experiments
        .is_none());
    assert_eq!(fs::read_to_string(file).unwrap(), invalid);
}

#[test]
fn csharp_reference_matches_session_dashboard_content_and_correlations() {
    let fixture: Value =
        serde_json::from_str(include_str!("fixtures/creator-intelligence.json")).unwrap();
    let events: Vec<JournalEvent> = serde_json::from_value(fixture["events"].clone()).unwrap();
    assert!(
        events.len() > 500,
        "Must not use the UI's bounded event feed"
    );
    let now = DateTime::parse_from_rfc3339(fixture["now"].as_str().unwrap())
        .unwrap()
        .with_timezone(&Utc);
    let offset = FixedOffset::east_opt(fixture["offsetSeconds"].as_i64().unwrap() as i32).unwrap();
    let result =
        serde_json::to_value(analyze(&events, 30, now, |at| at.with_timezone(&offset))).unwrap();
    for key in ["latest", "dashboard", "content", "correlation"] {
        assert_equivalent(&result[key], &fixture[key], key);
    }
}
