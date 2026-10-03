use ccs_overlay_server::{OverlayLayoutStore, RealtimeHub};
use serde_json::{json, Value};
use std::sync::Arc;

#[tokio::test]
async fn goal_sync_matches_csharp_for_all_layouts_and_preserves_unrelated_props() {
    let root = tempfile::tempdir().unwrap();
    let hub = Arc::new(RealtimeHub::new());
    let store = OverlayLayoutStore::with_hub(root.path(), hub.clone());
    store
        .save(
            "one",
            &json!({"future":true,"items":[
                {"id":"f","type":"GOAL-BAR","props":{"kind":"followers","current":9,"color":"red"}},
                {"id":"s","type":"goal-bar","props":{"Kind":"SUBS","Current":12}},
                {"id":"d","type":"goal-bar","props":{"kind":"bits"}},
                {"id":"c","type":"goal-bar","props":{"kind":"custom"}},
                {"type":"chat","props":{"target":123}}
            ]}),
        )
        .await
        .unwrap();
    store
        .save("two", &json!({"items":[{"type":"goal-bar","props":{}}]}))
        .await
        .unwrap();
    std::fs::write(root.path().join("broken.json"), b"{broken").unwrap();
    let mut events = hub.subscribe();
    let settings = json!({"FollowerGoal":{"Title":"Follower","Target":500},"SubGoal":{"Title":"Subs","Target":50},"DonationGoal":{"Title":"Support","Reason":"Mikrofon","Target":0}});
    let warnings = store.synchronize_goals(&settings).await;
    assert_eq!(warnings.len(), 1);
    let one = store.load("one").await.unwrap();
    assert_eq!(one["future"], true);
    for (index, label, target) in [
        (0, "Follower", 500.0),
        (1, "Subs", 50.0),
        (2, "Support · Mikrofon", 1.0),
        (3, "Support · Mikrofon", 1.0),
    ] {
        assert_eq!(one["items"][index]["props"]["label"], label);
        assert_eq!(one["items"][index]["props"]["target"], target);
        assert!(one["items"][index]["props"].get("current").is_none());
        assert!(one["items"][index]["props"].get("Current").is_none());
    }
    assert_eq!(one["items"][0]["props"]["color"], "red");
    assert_eq!(one["items"][4]["props"]["target"], 123);
    assert_eq!(
        store.load("two").await.unwrap()["items"][0]["props"]["target"],
        500.0
    );
    assert_eq!(
        std::fs::read(root.path().join("broken.json")).unwrap(),
        b"{broken"
    );
    for _ in 0..2 {
        let event: Value = serde_json::from_str(&events.recv().await.unwrap()).unwrap();
        assert_eq!(event["type"], "app.overlay.layout");
        assert!(serde_json::from_str::<Value>(event["data"]["layout"].as_str().unwrap()).is_ok());
    }
    store.synchronize_goals(&settings).await;
    assert!(events.try_recv().is_err());
}
