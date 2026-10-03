use ccs_overlay_server::RealtimeHub;
use serde_json::json;

#[test]
fn csharp_list_and_native_envelope_restore_only_chat_and_persist_as_csharp_list() {
    let dir = tempfile::tempdir().unwrap();
    let message = json!({"source":"twitch","type":"channel.chat.message","data":{"messageId":"one","userLogin":"Alice","custom":"retain"},"future":{"keep":true}});
    for (index, payload) in [
        json!([message.clone(), {"source":"app","type":"app.alert"}]),
        json!({"events":[message.clone()]}),
    ]
    .iter()
    .enumerate()
    {
        let path = dir.path().join(format!("history-{index}.json"));
        std::fs::write(&path, payload.to_string()).unwrap();
        let hub = RealtimeHub::new();
        hub.configure_history(path.clone()).unwrap();
        assert_eq!(hub.history()["events"], json!([message.clone()]));
        hub.publish(&json!({"source":"twitch","type":"channel.chat.message","data":{"messageId":"two","userLogin":"bob"}}));
        hub.flush_history().unwrap();
        let stored: serde_json::Value =
            serde_json::from_slice(&std::fs::read(&path).unwrap()).unwrap();
        assert_eq!(stored.as_array().unwrap().len(), 2);
        assert_eq!(stored[0], message);
        let second = RealtimeHub::new();
        second.configure_history(path).unwrap();
        second.publish(&json!({"source":"twitch","type":"channel.chat.clear_user_messages","data":{"targetUserLogin":"aLiCe"}}));
        assert_eq!(second.history()["events"].as_array().unwrap().len(), 1);
        second.flush_history().unwrap();
    }
}

#[test]
fn changing_history_path_flushes_old_buffer_and_preserves_a_corrupt_target() {
    let dir = tempfile::tempdir().unwrap();
    let old = dir.path().join("old.json");
    let next = dir.path().join("next.json");
    let hub = RealtimeHub::new();
    hub.configure_history(old.clone()).unwrap();
    hub.publish(
        &json!({"source":"twitch","type":"channel.chat.message","data":{"messageId":"old"}}),
    );
    std::fs::write(&next, "corrupt").unwrap();
    assert!(hub.configure_history(next.clone()).is_err());
    assert_eq!(std::fs::read_to_string(&next).unwrap(), "corrupt");
    assert_eq!(hub.history()["events"][0]["data"]["messageId"], "old");
    hub.flush_history().unwrap();
    std::fs::write(
        &next,
        json!([{ "source":"twitch", "type":"channel.chat.message", "data":{"messageId":"new"} }])
            .to_string(),
    )
    .unwrap();
    hub.configure_history(next.clone()).unwrap();
    assert_eq!(hub.history()["events"][0]["data"]["messageId"], "new");
    let stored: serde_json::Value = serde_json::from_slice(&std::fs::read(&old).unwrap()).unwrap();
    assert_eq!(stored[0]["data"]["messageId"], "old");
    hub.publish(&json!({"source":"app","type":"app.chat.clear"}));
    hub.flush_history().unwrap();
    assert_eq!(std::fs::read_to_string(next).unwrap(), "[]");
}

#[test]
fn chat_write_failure_retains_the_dirty_buffer_until_a_successful_retry() {
    let dir = tempfile::tempdir().unwrap();
    let parent = dir.path().join("blocked");
    let path = parent.join("chat-history.json");
    let hub = RealtimeHub::new();
    hub.configure_history(path.clone()).unwrap();
    hub.publish(
        &json!({"source":"twitch","type":"channel.chat.message","data":{"messageId":"retry"}}),
    );
    std::fs::write(&parent, "file blocks directory").unwrap();
    assert!(hub.flush_history().is_err());
    assert!(hub.live.data.read().unwrap()["chatHistoryError"].is_string());
    hub.configure_history(path.clone()).unwrap();
    assert!(hub.live.data.read().unwrap()["chatHistoryError"].is_string());
    assert_eq!(hub.history()["events"][0]["data"]["messageId"], "retry");
    std::fs::remove_file(parent).unwrap();
    hub.flush_history().unwrap();
    assert!(hub.live.data.read().unwrap()["chatHistoryError"].is_null());
    let disk: serde_json::Value = serde_json::from_slice(&std::fs::read(path).unwrap()).unwrap();
    assert_eq!(disk[0]["data"]["messageId"], "retry");
}

#[test]
fn invalid_chat_sources_and_empty_deletion_ids_do_not_modify_csharp_history() {
    let hub = RealtimeHub::new();
    hub.publish(
        &json!({"source":"app","type":"channel.chat.message","data":{"messageId":"invalid"}}),
    );
    assert!(hub.history()["events"].as_array().unwrap().is_empty());
    hub.publish(
        &json!({"source":"twitch","type":"channel.chat.message","data":{"messageId":"AbC"}}),
    );
    hub.publish(
        &json!({"source":"twitch","type":"channel.chat.message","data":{"userLogin":"legacy"}}),
    );
    hub.publish(
        &json!({"source":"twitch","type":"channel.chat.message_delete","data":{"messageId":null}}),
    );
    assert_eq!(hub.history()["events"].as_array().unwrap().len(), 2);
    hub.publish(&json!({"source":"twitch","type":"channel.chat.message_delete","data":{"messageId":" abc "}}));
    assert_eq!(hub.history()["events"].as_array().unwrap().len(), 1);
    assert_eq!(hub.history()["events"][0]["data"]["userLogin"], "legacy");
}
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
