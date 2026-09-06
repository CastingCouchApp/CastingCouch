use ccs_overlay_server::RealtimeHub;
use serde_json::{json, Value};
fn contains_fields(reference: &Value, actual: &Value, path: &str) {
    if let Some(map) = reference.as_object() {
        for (k, v) in map {
            assert!(actual.get(k).is_some(), "missing {path}/{k}");
            contains_fields(v, &actual[k], &format!("{path}/{k}"));
        }
    }
}
#[test]
fn empty_snapshot_has_all_legacy_fields() {
    let hub = RealtimeHub::new();
    let reference: Value = serde_json::from_str(include_str!(
        "../../ccs-core/tests/fixtures/csharp-overlay.json"
    ))
    .unwrap();
    contains_fields(&reference, &hub.live.data.read().unwrap(), "");
}
#[test]
fn event_updates_and_partial_snapshots_preserve_other_data() {
    let hub = RealtimeHub::new();
    hub.live
        .merge_snapshot(&json!({"stream":{"isLive":true,"elapsedSeconds":12}}));
    hub.publish(&json!({"type":"channel.follow","summary":"New follower","data":{"user":"Alice"}}));
    hub.publish(&json!({"type":"channel.chat.message","data":{"messageId":"1","userId":"u"}}));
    hub.live.merge_snapshot(&json!({"music":{"title":"Track"}}));
    let data = hub.live.data.read().unwrap();
    assert_eq!(data["stats"]["followersGained"], 1);
    assert_eq!(data["stats"]["chatMessages"], 1);
    assert_eq!(data["twitch"]["lastFollower"], "Alice");
    assert_eq!(data["stream"]["phase"], "Live");
    assert!(data["stream"]["startedAt"].is_string());
    assert_eq!(data["music"]["title"], "Track");
}
#[tokio::test]
async fn file_writer_preserves_custom_fields_and_hardlinks() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("overlay-data.json");
    let alias = dir.path().join("linked.json");
    std::fs::write(
        &path,
        r#"{"custom":{"keep":true},"stream":{"isLive":true}}"#,
    )
    .unwrap();
    std::fs::hard_link(&path, &alias).unwrap();
    let hub = RealtimeHub::new();
    hub.live.write_snapshot(&path).await.unwrap();
    let original: Value = serde_json::from_slice(&std::fs::read(&path).unwrap()).unwrap();
    let linked: Value = serde_json::from_slice(&std::fs::read(&alias).unwrap()).unwrap();
    assert_eq!(original, linked);
    assert_eq!(linked["custom"]["keep"], true);
    assert_eq!(hub.live.data.read().unwrap()["custom"]["keep"], true);
    assert_eq!(linked["stream"]["isLive"], false);
}

#[test]
fn twitch_snapshot_uses_helix_totals_and_configured_goals() {
    let hub = RealtimeHub::new();
    hub.live.merge_snapshot(&json!({"stream":{"isLive":true}}));
    hub.live.update_twitch(&json!({"ChannelName":"channel","FollowerGoal":{"Title":"Goal","Current":4,"Target":50,"Reason":"Test"}}),&json!({"data":[{"title":"Title","game_name":"Category"}]}),&json!({"data":[{"viewer_count":12}]}),&json!({"total":30}),&json!({"total":7}));
    let data = hub.live.data.read().unwrap();
    assert_eq!(data["twitch"]["followers"], 30);
    assert_eq!(data["twitch"]["followerGoalState"]["current"], 30);
    assert_eq!(data["twitch"]["subGoalState"]["current"], 7);
    assert_eq!(data["twitch"]["followerGoalState"]["target"], 50);
    assert_eq!(data["stream"]["viewerCount"], 12);
    assert_eq!(data["stats"]["peakViewers"], 12);
}
