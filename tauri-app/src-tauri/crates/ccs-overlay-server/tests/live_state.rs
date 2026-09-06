use ccs_overlay_server::RealtimeHub;
use serde_json::json;
#[test]
fn chat_history_deduplicates_and_applies_moderation_before_persisting() {
    let dir = tempfile::tempdir().unwrap();
    let hub = RealtimeHub::new();
    hub.configure_history(dir.path().join("chat-history.json"))
        .unwrap();
    let message = json!({"source":"twitch","type":"channel.chat.message","data":{"messageId":"m1","userId":"u1","userName":"Alice","parts":"[]"}});
    hub.publish(&message);
    hub.publish(&message);
    assert_eq!(hub.history()["events"].as_array().unwrap().len(), 1);
    hub.flush_history().unwrap();
    let restored = RealtimeHub::new();
    restored
        .configure_history(dir.path().join("chat-history.json"))
        .unwrap();
    assert_eq!(restored.history(), hub.history());
    restored.publish(
        &json!({"type":"channel.chat.clear_user_messages","data":{"target_user_id":"u1"}}),
    );
    assert!(restored.history()["events"].as_array().unwrap().is_empty());
}
#[test]
fn countdown_stops_and_late_clients_get_current_state() {
    let hub = RealtimeHub::new();
    hub.set_countdown(60, "Gleich geht es los").unwrap();
    assert_eq!(hub.countdown()["data"]["isRunning"], "true");
    hub.set_countdown(0, "").unwrap();
    assert_eq!(hub.countdown()["data"]["remainingSeconds"], "0");
    assert!(hub.set_countdown(-1, "").is_err());
}

#[test]
fn countdown_snapshot_has_numeric_values_for_canvas_widgets() {
    let hub = RealtimeHub::new();
    hub.set_countdown(30, "Start").unwrap();
    let state = hub.live.countdown_state();
    assert_eq!(state["isRunning"], true);
    assert_eq!(state["totalSeconds"], 30);
    assert_eq!(state["mode"], "manual");
    assert!(state["remainingSeconds"].as_i64().unwrap() > 0);
}
