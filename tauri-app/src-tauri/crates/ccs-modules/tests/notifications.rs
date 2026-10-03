use ccs_modules::notifications::NotificationRuntime;
use serde_json::{json, Value};
use std::{fs, sync::Arc};

#[test]
fn repaired_legacy_cache_is_merged_with_live_events_on_retry() {
    let root = tempfile::tempdir().unwrap();
    let file = root.path().join("notifications.json");
    fs::write(&file, "broken").unwrap();
    let journal = NotificationRuntime::new(root.path());
    journal.record("new event", "Info");
    fs::write(
        &file,
        format!(
            "\u{feff}{}",
            json!([{"Timestamp":"2026-10-03T10:00:00Z","Message":"repaired","Extra":true}])
        ),
    )
    .unwrap();
    journal.retry().unwrap();
    journal.retry().unwrap();
    assert_eq!(journal.snapshot("Alle").unwrap().total, 2);
    assert_eq!(
        NotificationRuntime::new(root.path())
            .snapshot("Alle")
            .unwrap()
            .total,
        2
    );
}

#[test]
fn service_changes_and_stream_end_ticks_record_transitions_without_spam() {
    use ccs_modules::{stream_end::StreamEndSnapshot, ConnectionState, ServiceStatus};
    let root = tempfile::tempdir().unwrap();
    let journal = NotificationRuntime::new(root.path());
    let mut status = ServiceStatus::disconnected("obs", "OBS");
    journal.observe_service(&status);
    status.state = ConnectionState::Connecting;
    journal.observe_service(&status);
    status.state = ConnectionState::Connected;
    journal.observe_service(&status);
    journal.observe_service(&status);
    status.state = ConnectionState::Error;
    status.detail = "connection lost".into();
    journal.observe_service(&status);
    journal.observe_service(&status);
    let mut end = StreamEndSnapshot {
        run_id: 1,
        active: true,
        phase: "end_scene".into(),
        status: "Endszene läuft".into(),
        remaining_seconds: 60,
        total_seconds: 60,
        ..Default::default()
    };
    journal.observe_stream_end(&end);
    end.remaining_seconds = 59;
    end.status = "Endszene läuft: 59 Sekunden".into();
    journal.observe_stream_end(&end);
    end.phase = "failed".into();
    end.error = Some("OBS stop failed".into());
    end.warnings
        .push("Musik konnte nicht pausiert werden".into());
    journal.observe_stream_end(&end);
    journal.observe_stream_end(&end);
    assert_eq!(journal.snapshot("Alle").unwrap().total, 5);
    assert_eq!(journal.snapshot("Fehler").unwrap().entries.len(), 2);
    assert_eq!(journal.snapshot("Warnungen").unwrap().entries.len(), 1);
}

#[test]
fn csharp_journal_retains_unknown_fields_limits_filters_and_survives_read_clear_restart() {
    let root = tempfile::tempdir().unwrap();
    let entries = (0..270).map(|n|json!({"Timestamp":format!("2026-10-03T12:{:02}:{:02}+02:00",n/60,n%60),"Severity":if n%2==0 {"Warnung"}else{"Info"},"Message":format!("message {n}"),"IsRead":n%3==0,"Future":{"n":n}})).collect::<Vec<_>>();
    fs::write(
        root.path().join("notifications.json"),
        serde_json::to_vec(&entries).unwrap(),
    )
    .unwrap();
    let journal = NotificationRuntime::new(root.path());
    let all = journal.snapshot("Alle").unwrap();
    assert_eq!(all.total, 250);
    assert_eq!(all.entries.len(), 100);
    assert_eq!(all.entries[0].message, "message 269");
    let warnings = journal.snapshot("Warnungen").unwrap();
    assert_eq!(warnings.entries.len(), 100);
    assert!(warnings.entries.iter().all(|n| n.severity == "Warnung"));
    assert!(journal.snapshot("unsupported").is_err());
    journal.mark_all_read().unwrap();
    assert_eq!(
        NotificationRuntime::new(root.path())
            .snapshot("Alle")
            .unwrap()
            .unread_count,
        0
    );
    let saved: Vec<Value> =
        serde_json::from_slice(&fs::read(root.path().join("notifications.json")).unwrap()).unwrap();
    assert_eq!(saved[0]["Future"], json!({"n":20}));
    assert_eq!(saved[0]["Timestamp"], entries[20]["Timestamp"]);
    journal.record("late warning", "Warning");
    assert_eq!(journal.snapshot("Alle").unwrap().unread_count, 1);
    journal.clear().unwrap();
    assert_eq!(
        NotificationRuntime::new(root.path())
            .snapshot("Alle")
            .unwrap()
            .total,
        0
    );
}

#[test]
fn corrupt_journal_is_preserved_until_explicit_retry_creates_a_backup() {
    let root = tempfile::tempdir().unwrap();
    let file = root.path().join("notifications.json");
    fs::write(&file, b"broken C# cache").unwrap();
    let journal = NotificationRuntime::new(root.path());
    journal.record("OBS actually started", "Info");
    assert_eq!(fs::read(&file).unwrap(), b"broken C# cache");
    assert_eq!(journal.snapshot("Alle").unwrap().total, 1);
    assert!(!journal.snapshot("Alle").unwrap().warnings.is_empty());
    assert!(journal.clear().is_err());
    journal.retry().unwrap();
    let backup = journal.snapshot("Alle").unwrap().recovery_backup.unwrap();
    assert_eq!(fs::read(backup).unwrap(), b"broken C# cache");
    assert!(journal.snapshot("Alle").unwrap().warnings.is_empty());
    assert_eq!(
        NotificationRuntime::new(root.path())
            .snapshot("Alle")
            .unwrap()
            .entries[0]
            .message,
        "OBS actually started"
    );
}

#[test]
fn write_failures_keep_live_notifications_and_failed_edits_do_not_claim_success() {
    let root = tempfile::tempdir().unwrap();
    let journal = NotificationRuntime::new(root.path());
    journal.record("first", "Info");
    let file = root.path().join("notifications.json");
    fs::remove_file(&file).unwrap();
    fs::create_dir(&file).unwrap();
    journal.record("second", "Error");
    assert_eq!(journal.snapshot("Alle").unwrap().total, 2);
    assert!(journal.mark_all_read().is_err());
    assert!(journal.clear().is_err());
    assert_eq!(journal.snapshot("Alle").unwrap().unread_count, 2);
    fs::remove_dir(&file).unwrap();
    journal.retry().unwrap();
    assert!(journal.snapshot("Alle").unwrap().warnings.is_empty());
    assert_eq!(
        NotificationRuntime::new(root.path())
            .snapshot("Fehler")
            .unwrap()
            .entries[0]
            .message,
        "second"
    );
}

#[test]
fn concurrent_producers_and_duplicate_output_events_do_not_lose_or_repeat_notifications() {
    let root = tempfile::tempdir().unwrap();
    let journal = Arc::new(NotificationRuntime::new(root.path()));
    let tasks = (0..12)
        .map(|n| {
            let journal = journal.clone();
            std::thread::spawn(move || journal.record(&format!("event {n}"), "Info"))
        })
        .collect::<Vec<_>>();
    for task in tasks {
        task.join().unwrap();
    }
    let at = chrono::Utc::now();
    journal.observe_stream(false, at);
    journal.observe_stream(true, at);
    journal.observe_stream(true, at);
    journal.observe_stream(false, at);
    journal.observe_stream(true, at - chrono::Duration::seconds(1));
    assert_eq!(journal.snapshot("Alle").unwrap().total, 14);
    assert_eq!(
        NotificationRuntime::new(root.path())
            .snapshot("Alle")
            .unwrap()
            .total,
        14
    );
}
