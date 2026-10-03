use ccs_modules::stream_history::StreamHistoryRuntime;
use ccs_overlay_server::RealtimeHub;
use chrono::{TimeZone, Utc};
use serde_json::{json, Value};
use std::sync::Arc;
fn at(seconds: i64) -> chrono::DateTime<Utc> {
    Utc.with_ymd_and_hms(2026, 10, 3, 10, 0, 0).unwrap() + chrono::Duration::seconds(seconds)
}
fn live(active: bool) -> Value {
    json!({"stream":{"available":true,"isLive":active,"elapsedSeconds":0},"obs":{"currentScene":"Live"}})
}
fn metrics(viewers: u64, followers: u64, seconds: i64) -> Value {
    json!({"connected":true,"viewerCount":{"value":viewers,"at":at(seconds).to_rfc3339(),"error":null},"followers":{"value":followers,"error":null},"title":"Title","category":"Game","channelError":null})
}

#[test]
fn protected_history_does_not_block_confirmed_obs_live_data() {
    let root = tempfile::tempdir().unwrap();
    std::fs::create_dir_all(root.path().join("StreamHistory")).unwrap();
    let checkpoint = root.path().join("StreamHistory/active-session.json");
    std::fs::write(&checkpoint, "damaged").unwrap();
    let hub = Arc::new(RealtimeHub::new());
    let history = StreamHistoryRuntime::new(root.path().into(), hub.clone());
    assert!(history.observe_stream_event(true, at(0)).is_err());
    assert_eq!(hub.live.data.read().unwrap()["stream"]["phase"], "Live");
    assert!(history.observe_stream_event(false, at(1)).is_err());
    assert_eq!(hub.live.data.read().unwrap()["stream"]["phase"], "Idle");
    assert_eq!(std::fs::read_to_string(&checkpoint).unwrap(), "damaged");
}

#[test]
fn stream_events_use_latest_context_and_final_followers_even_before_first_poll() {
    let root = tempfile::tempdir().unwrap();
    let history = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    history
        .update_context(
            &metrics(8, 100, 0),
            &json!({"RaidOnStreamEnd":true,"SelectedRaidChannel":"target"}),
        )
        .unwrap();
    history.observe_stream_event(true, at(1)).unwrap();
    assert_eq!(
        history.snapshot(None).unwrap().active.unwrap()["Title"],
        "Title"
    );
    history
        .observe(&live(false), &metrics(8, 103, 2), &json!({}), at(2))
        .unwrap();
    let row = &history.snapshot(None).unwrap().sessions[0];
    assert_eq!(row["FollowersGained"], 3);
    assert_eq!(row["RaidTarget"], "target");
}

#[test]
fn delayed_obs_poll_cannot_end_or_restart_a_session_after_a_newer_stream_event() {
    let root = tempfile::tempdir().unwrap();
    let history = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    history.observe_stream_event(true, at(10)).unwrap();
    history
        .observe_polled(&live(false), &metrics(8, 0, 11), &json!({}), at(9), at(11))
        .unwrap();
    assert!(history.snapshot(None).unwrap().active.is_some());
    history.observe_stream_event(false, at(12)).unwrap();
    history
        .observe_polled(&live(true), &metrics(8, 0, 13), &json!({}), at(11), at(13))
        .unwrap();
    let snapshot = history.snapshot(None).unwrap();
    assert!(snapshot.active.is_none());
    assert_eq!(snapshot.sessions.len(), 1);
    assert_eq!(snapshot.sessions[0]["DurationSeconds"], 2);
}

#[test]
fn journal_payloads_match_csharp_without_retaining_chat_content_and_deduplicate_eventsub() {
    let root = tempfile::tempdir().unwrap();
    let history = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    history
        .observe(&live(true), &metrics(8, 10, 0), &json!({}), at(0))
        .unwrap();
    history.record(&json!({"source":"twitch","type":"channel.chat.message","at":at(1).to_rfc3339(),"summary":"secret-text","data":{"messageId":"chat","userName":"Alice","text":"secret-text","parts":[]}}));
    history.record(&json!({"source":"app","type":"app.obs.scene","at":at(2).to_rfc3339(),"data":{"scene":"Talk"}}));
    for seconds in [3, 4] {
        history.record(&json!({"source":"twitch","type":"channel.subscribe","at":at(seconds).to_rfc3339(),"summary":"Alice subscribed","data":{"eventSubMessageId":"sub-id"}}));
    }
    history.record(&json!({"source":"twitch","type":"channel.follow","at":at(5).to_rfc3339(),"summary":"Alice follows","data":{}}));
    let snapshot = history.snapshot(None).unwrap();
    let payload =
        |kind: &str| snapshot.events.iter().find(|e| e["Type"] == kind).unwrap()["Payload"].clone();
    assert_eq!(
        payload("twitch.chat.message"),
        json!({"user":"Alice","scene":"Live","viewers":8})
    );
    assert_eq!(
        payload("obs.scene.changed"),
        json!({"scene":"Talk","viewers":8})
    );
    assert_eq!(
        payload("twitch.event"),
        json!({"type":"channel.follow","summary":"Alice follows","scene":"Talk","viewers":8})
    );
    assert_eq!(payload("twitch.follow"), json!({"Summary":"Alice follows"}));
    assert_eq!(snapshot.active.unwrap()["NewSubscriptions"], 1);
    assert!(
        !std::fs::read_to_string(root.path().join("CreatorIntelligence/2026-10/events.jsonl"))
            .unwrap()
            .contains("secret-text")
    );
    drop(history);
    let restarted = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    restarted.record(&json!({"source":"twitch","type":"channel.subscribe","at":at(6).to_rfc3339(),"data":{"eventSubMessageId":"sub-id"}}));
    assert_eq!(
        restarted.snapshot(None).unwrap().active.unwrap()["NewSubscriptions"],
        1
    );
}

#[test]
fn exports_keep_receipt_order_and_report_uses_raw_case_sensitive_category_averages() {
    let root = tempfile::tempdir().unwrap();
    std::fs::create_dir_all(root.path().join("StreamHistory")).unwrap();
    let newer = json!({"StartedAt":at(100).to_rfc3339(),"DurationSeconds":3600,"PeakViewers":20,"AverageViewers":10.04,"Category":"Game","Title":"Newer"});
    let older = json!({"StartedAt":at(0).to_rfc3339(),"DurationSeconds":3600,"PeakViewers":40,"AverageViewers":10.03,"Category":"game","Title":"Last received"});
    let third = json!({"StartedAt":at(50).to_rfc3339(),"DurationSeconds":3600,"PeakViewers":30,"AverageViewers":10.035,"Category":"Other","Title":"Third"});
    std::fs::write(
        root.path().join("StreamHistory/history.jsonl"),
        format!("{newer}\n{third}\n{older}\nbroken\n"),
    )
    .unwrap();
    let history = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    let csv = root.path().join("history.csv");
    history.export("csv", &csv).unwrap();
    let output = std::fs::read_to_string(csv).unwrap();
    assert!(output.find("Newer").unwrap() < output.find("Third").unwrap());
    assert!(output.find("Third").unwrap() < output.find("Last received").unwrap());
    assert!(history
        .latest_summary()
        .unwrap()
        .contains("Titel: Last received"));
    let html = root.path().join("history.html");
    history.export("html", &html).unwrap();
    let output = std::fs::read_to_string(html).unwrap();
    assert!(output.contains("Zuschauerdurchschnitt: <strong>Game</strong>"));
    assert!(output.contains("mittleren Peak von 30.0"));
    assert!(output.find("Newer").unwrap() < output.find("Third").unwrap());
}

#[test]
fn sessions_count_confirmed_samples_followers_resubs_chat_alerts_and_ignore_disconnects() {
    let root = tempfile::tempdir().unwrap();
    let hub = Arc::new(RealtimeHub::new());
    let history = StreamHistoryRuntime::new(root.path().into(), hub.clone());
    history
        .observe(
            &live(true),
            &metrics(10, 100, 0),
            &json!({"RaidOnStreamEnd":true,"SelectedRaidChannel":"target"}),
            at(0),
        )
        .unwrap();
    for (kind, data) in [
        ("channel.chat.message", json!({"messageId":"m"})),
        ("channel.chat.message", json!({"messageId":"m"})),
        ("channel.subscribe", json!({})),
        ("channel.subscription.message", json!({})),
        ("channel.subscription.gift", json!({"total":"3"})),
        ("channel.cheer", json!({"bits":"42"})),
        ("channel.raid", json!({"viewers":"9"})),
        ("app.alert", json!({"alertType":"Follow"})),
    ] {
        history.record(&json!({"source":if kind.starts_with("app"){"app"}else{"twitch"},"type":kind,"at":at(1).to_rfc3339(),"summary":"Event","data":data}));
    }
    history
        .observe(&live(true), &metrics(20, 105, 30), &json!({}), at(30))
        .unwrap();
    history
        .observe(
            &json!({"stream":{"available":false,"isLive":false}}),
            &json!({"connected":false}),
            &json!({}),
            at(60),
        )
        .unwrap();
    assert!(history.snapshot(None).unwrap().active.is_some());
    let active = history.snapshot(None).unwrap().active.unwrap();
    assert_eq!(active["DurationSeconds"], 30);
    assert_eq!(active["ObservationAvailable"], false);
    history
        .observe(&live(false), &metrics(20, 105, 30), &json!({}), at(90))
        .unwrap();
    history
        .observe(&live(false), &metrics(20, 105, 30), &json!({}), at(100))
        .unwrap();
    let snapshot = history.snapshot(None).unwrap();
    assert_eq!(snapshot.sessions.len(), 1);
    let row = &snapshot.sessions[0];
    for (key, value) in [
        ("DurationSeconds", 90),
        ("PeakViewers", 20),
        ("FollowersGained", 5),
        ("ChatMessages", 1),
        ("NewSubscriptions", 2),
        ("GiftSubscriptions", 3),
        ("BitsCheered", 42),
        ("IncomingRaids", 1),
        ("AlertsPlayed", 1),
    ] {
        assert_eq!(row[key], value, "{key}");
    }
    assert_eq!(row["AverageViewers"], 15.0);
    assert_eq!(row["RaidTarget"], "target");
    assert_eq!(row["Title"], "Title");
    assert_eq!(row["ViewerSamples"].as_array().unwrap().len(), 2);
    assert_eq!(
        hub.live.data.read().unwrap()["stats"]["newSubscriptions"],
        2
    );
    assert_eq!(
        std::fs::read_to_string(root.path().join("StreamHistory/history.jsonl"))
            .unwrap()
            .lines()
            .count(),
        1
    );
    let events =
        std::fs::read_to_string(root.path().join("CreatorIntelligence/2026-10/events.jsonl"))
            .unwrap();
    assert!(events.contains("\"Type\":\"twitch.viewer.sample\""));
    assert!(events.contains("\"Type\":\"twitch.chat.message\""));
    assert!(events.contains("\"Type\":\"session.ended\""));
    let restarted = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    assert_eq!(
        restarted.snapshot(None).unwrap().sessions,
        snapshot.sessions
    );
}

#[test]
fn checkpoint_resumes_matching_stream_and_finishes_interrupted_at_last_observation() {
    let root = tempfile::tempdir().unwrap();
    let history = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    history
        .observe(&live(true), &metrics(8, 0, 0), &json!({}), at(0))
        .unwrap();
    let mut continued = live(true);
    continued["stream"]["elapsedSeconds"] = json!(30);
    history
        .observe(&continued, &metrics(12, 1, 30), &json!({}), at(30))
        .unwrap();
    drop(history);
    let restart = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    assert_eq!(
        restart.snapshot(None).unwrap().active.as_ref().unwrap()["Recovered"],
        true
    );
    continued["stream"]["elapsedSeconds"] = json!(60);
    restart
        .observe(&continued, &metrics(10, 1, 60), &json!({}), at(60))
        .unwrap();
    assert_eq!(
        restart.snapshot(None).unwrap().active.as_ref().unwrap()["Recovered"],
        false
    );
    drop(restart);
    let offline = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    offline
        .observe(&live(false), &json!({}), &json!({}), at(3600))
        .unwrap();
    let snapshot = offline.snapshot(None).unwrap();
    assert_eq!(snapshot.sessions[0]["DurationSeconds"], 60);
    assert_eq!(snapshot.sessions[0]["Interrupted"], true);
}

#[test]
fn legacy_history_and_journals_preserve_rows_skip_damage_and_export_compatible_csv() {
    let root = tempfile::tempdir().unwrap();
    std::fs::create_dir_all(root.path().join("StreamHistory")).unwrap();
    std::fs::create_dir_all(root.path().join("CreatorIntelligence/2026-09")).unwrap();
    let older = json!({"StartedAt":"2026-09-01T10:00:00+02:00","EndedAt":"2026-09-01T11:00:00+02:00","DurationSeconds":3600,"AverageViewers":10,"PeakViewers":20,"FollowersGained":2,"Category":"Game","Title":"A;B\nC","Future":{"keep":true}});
    let newer = json!({"StartedAt":"2026-09-02T10:00:00+02:00","DurationSeconds":7200,"AverageViewers":20,"PeakViewers":30,"FollowersGained":3,"Category":"game"});
    let bytes = format!("\u{feff}{older}\r\nbroken\r\n{newer}\r\n");
    let file = root.path().join("StreamHistory/history.jsonl");
    std::fs::write(&file, &bytes).unwrap();
    std::fs::write(root.path().join("CreatorIntelligence/2026-09/events.jsonl"),"{\"TimestampUtc\":\"2026-09-01T08:00:00Z\",\"SessionId\":\"legacy\",\"Type\":\"twitch.event\",\"Payload\":{\"summary\":\"Follow\",\"extra\":7}}\ninvalid\n").unwrap();
    let history = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    let snapshot = history.snapshot(Some("legacy")).unwrap();
    assert_eq!(snapshot.sessions[1], older);
    assert_eq!(snapshot.events[0]["Payload"]["extra"], 7);
    assert_eq!(snapshot.statistics["totalStreams"], 2);
    assert!((snapshot.statistics["averageViewers"].as_f64().unwrap() - 50.0 / 3.0).abs() < 0.001);
    assert_eq!(snapshot.statistics["followers"], 5);
    assert_eq!(
        snapshot.statistics["categories"].as_array().unwrap().len(),
        1
    );
    assert!(!snapshot.warnings.is_empty());
    let export = root.path().join("export.csv");
    history.export("csv", &export).unwrap();
    let csv = std::fs::read_to_string(&export).unwrap();
    assert!(csv.starts_with('\u{feff}'));
    assert!(csv.contains("StartedAt;EndedAt;DurationSeconds;PeakViewers;AverageViewers;FollowersGained;ChatMessages;Category;Title"));
    assert!(csv.contains("A,B C"));
    assert_eq!(std::fs::read_to_string(file).unwrap(), bytes);
    let report = root.path().join("report.html");
    history.export("html", &report).unwrap();
    assert!(std::fs::read_to_string(report)
        .unwrap()
        .contains("Twitch Stream-Report"));
    assert!(history
        .export("csv", &root.path().join("StreamHistory/history.jsonl"))
        .is_err());
}

#[test]
fn disk_failure_is_visible_retry_saves_once_and_corrupt_checkpoint_is_preserved() {
    let root = tempfile::tempdir().unwrap();
    std::fs::write(root.path().join("StreamHistory"), "blocked").unwrap();
    let history = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    assert!(history
        .observe(&live(true), &metrics(3, 0, 0), &json!({}), at(0))
        .is_err());
    assert!(!history.snapshot(None).unwrap().warnings.is_empty());
    std::fs::remove_file(root.path().join("StreamHistory")).unwrap();
    history.retry().unwrap();
    history
        .observe(&live(false), &metrics(3, 0, 0), &json!({}), at(30))
        .unwrap();
    history.retry().unwrap();
    assert_eq!(history.snapshot(None).unwrap().sessions.len(), 1);
    let checkpoint = root.path().join("StreamHistory/active-session.json");
    std::fs::write(&checkpoint, "damaged").unwrap();
    let broken = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    assert!(broken
        .observe(&live(true), &metrics(0, 0, 40), &json!({}), at(40))
        .is_err());
    assert_eq!(std::fs::read_to_string(&checkpoint).unwrap(), "damaged");
    assert!(!broken.snapshot(None).unwrap().warnings.is_empty());
    let malformed = json!({"version":1,"active":{"row":{"SessionId":"bad","StartedAt":at(0).to_rfc3339(),"ViewerSamples":null},"last_observed":at(0).to_rfc3339(),"sample_at":null,"follower_start":null,"scene":"Live"},"pending":[]}).to_string();
    std::fs::write(&checkpoint, &malformed).unwrap();
    let invalid = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    assert!(invalid
        .observe(&live(true), &metrics(0, 0, 0), &json!({}), at(0))
        .is_err());
    assert_eq!(std::fs::read_to_string(&checkpoint).unwrap(), malformed);
    let unknown = json!({"version":1,"active":null,"pending":[{"kind":"unknown","month":"2026-10","record":{"EventId":"id"}}]}).to_string();
    std::fs::write(&checkpoint, &unknown).unwrap();
    let invalid = StreamHistoryRuntime::new(root.path().into(), Arc::new(RealtimeHub::new()));
    assert!(invalid.retry().is_err());
    assert_eq!(std::fs::read_to_string(&checkpoint).unwrap(), unknown);
}

#[tokio::test]
async fn bridge_persists_real_overlay_events_and_shares_authoritative_counters_over_http_and_websocket(
) {
    use ccs_core::{AppPaths, JsonSettingsStore};
    use ccs_modules::overlay_bridge::OverlayEventBridge;
    use ccs_overlay_server::OverlayServer;
    use futures_util::StreamExt;
    use std::collections::BTreeMap;
    let root = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(root.path().into());
    let hub = Arc::new(RealtimeHub::new());
    let history = Arc::new(StreamHistoryRuntime::new(root.path().into(), hub.clone()));
    let bridge = OverlayEventBridge::new(hub.clone());
    bridge.set_stream_history(history.clone());
    hub.live.merge_snapshot(&live(true));
    history
        .observe(&live(true), &metrics(8, 10, 0), &json!({}), at(0))
        .unwrap();
    let server = OverlayServer::start(
        Arc::new(JsonSettingsStore::new(&paths.settings_file)),
        paths,
        hub.clone(),
        0,
    )
    .await
    .unwrap();
    let (mut ws, _) =
        tokio_tungstenite::connect_async(format!("ws://127.0.0.1:{}/ws", server.port))
            .await
            .unwrap();
    ws.next().await.unwrap().unwrap();
    ws.next().await.unwrap().unwrap();
    let emitted = bridge.from_twitch(
        "channel.subscription.message",
        "ReSub",
        at(2),
        BTreeMap::from([
            ("user_name".into(), "Alice".into()),
            ("eventSubMessageId".into(), "sub-id".into()),
        ]),
    );
    let message = ws.next().await.unwrap().unwrap();
    assert_eq!(
        serde_json::from_str::<Value>(message.to_text().unwrap()).unwrap(),
        emitted
    );
    bridge.from_twitch(
        "channel.subscription.message",
        "ReSub",
        at(3),
        BTreeMap::from([("eventSubMessageId".into(), "sub-id".into())]),
    );
    for seconds in [4, 5] {
        bridge.from_twitch(
            "channel.subscribe",
            "Sub",
            at(seconds),
            BTreeMap::from([("eventSubMessageId".into(), "new-sub-id".into())]),
        );
    }
    assert_eq!(
        hub.live.data.read().unwrap()["stats"]["newSubscriptions"],
        2
    );
    let http: Value = reqwest::get(format!(
        "http://127.0.0.1:{}/data/overlay-data.json",
        server.port
    ))
    .await
    .unwrap()
    .json()
    .await
    .unwrap();
    assert_eq!(http["stats"]["newSubscriptions"], 2);
    let snapshot = history.snapshot(None).unwrap();
    assert!(snapshot
        .events
        .iter()
        .any(|e| e["Type"] == "twitch.event" && e["Payload"]["summary"] == "ReSub"));
    ws.close(None).await.unwrap();
    server.stop();
}
