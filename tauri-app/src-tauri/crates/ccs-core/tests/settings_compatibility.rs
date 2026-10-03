use ccs_core::JsonSettingsStore;
use serde_json::json;

#[tokio::test]
async fn youtube_music_preferences_validate_and_preserve_imported_extra_fields() {
    let root = tempfile::tempdir().unwrap();
    let store = JsonSettingsStore::new(root.path().join("settings.json"));
    let original = store.read_value().await.unwrap();
    for (key, value) in [
        ("BridgePort", json!(0)),
        ("BridgePort", json!(65536)),
        ("BridgePort", json!("43831")),
        ("StateTimeoutSeconds", json!(2)),
        ("StateTimeoutSeconds", json!(121)),
        ("AutoConnect", json!(1)),
    ] {
        let mut invalid = original.clone();
        invalid["YouTubeMusic"][key] = value;
        assert!(store.save_edit(&original, &invalid).await.is_err(), "{key}");
        assert_eq!(store.read_value().await.unwrap(), original);
    }
    let mut next = original.clone();
    next["YouTubeMusic"] = json!({"BridgePort":43900,"StateTimeoutSeconds":30,"AutoConnect":false,"Future":{"keep":42}});
    store.save_edit(&original, &next).await.unwrap();
    let config = store.load().await.unwrap();
    assert_eq!(config.you_tube_music.bridge_port(), 43900);
    assert_eq!(config.you_tube_music.timeout_seconds(), 30);
    assert!(!config.you_tube_music.auto_connect());
    assert_eq!(
        store.read_value().await.unwrap()["YouTubeMusic"]["Future"]["keep"],
        42
    );
}

#[tokio::test]
async fn invalid_music_recovery_preferences_are_rejected_without_changing_settings() {
    let root = tempfile::tempdir().unwrap();
    let store = JsonSettingsStore::new(root.path().join("settings.json"));
    let original = store.read_value().await.unwrap();
    for (key, value) in [
        ("SavedStateMaxAgeMinutes", json!(0)),
        ("SavedStateCleanupIntervalMinutes", json!(1441)),
        ("HealthCheckIntervalSeconds", json!(301)),
        ("HealthMonitorEnabled", json!("false")),
    ] {
        let mut draft = original.clone();
        draft["Spotify"][key] = value;
        assert!(
            store.save_edit(&original, &draft).await.is_err(),
            "accepted {key}"
        );
        assert_eq!(store.read_value().await.unwrap(), original);
    }
}

#[tokio::test]
async fn typed_save_preserves_unknown_nested_wpf_fields() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("settings.json");
    std::fs::write(
        &path,
        json!({"SchemaVersion":2,"General":{"FutureSetting":{"a":1}},"FutureModule":{"b":2}})
            .to_string(),
    )
    .unwrap();
    let store = JsonSettingsStore::new(&path);
    let mut settings = store.load().await.unwrap();
    settings.general.theme_id = "classic".into();
    store.save(&settings).await.unwrap();
    let disk: serde_json::Value = serde_json::from_slice(&std::fs::read(path).unwrap()).unwrap();
    assert_eq!(disk["General"]["FutureSetting"]["a"], 1);
    assert_eq!(disk["FutureModule"]["b"], 2);
}

#[tokio::test]
async fn edits_merge_independent_changes_and_reject_conflicts() {
    let dir = tempfile::tempdir().unwrap();
    let store = JsonSettingsStore::new(dir.path().join("settings.json"));
    let base = store.read_value().await.unwrap();
    let mut first = base.clone();
    first["Branding"]["DisplayName"] = json!("First");
    store.save_edit(&base, &first).await.unwrap();
    let mut second = base.clone();
    second["General"]["ThemeId"] = json!("neon-night-market");
    store.save_edit(&base, &second).await.unwrap();
    assert_eq!(
        store.read_value().await.unwrap()["Branding"]["DisplayName"],
        "First"
    );
    second["Branding"]["DisplayName"] = json!("Conflict");
    assert!(store.save_edit(&base, &second).await.is_err());
    assert_eq!(
        store.read_value().await.unwrap()["Branding"]["DisplayName"],
        "First"
    );
}

#[tokio::test]
async fn unknown_array_fields_follow_identity_when_reordered() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("settings.json");
    std::fs::write(
        &path,
        json!({"SchemaVersion":2,"Overlay":{"Canvases":[
            {"Id":"first","Name":"First","Future":{"value":1}},
            {"Id":"second","Name":"Second","Future":{"value":2}}
        ]}})
        .to_string(),
    )
    .unwrap();
    let store = JsonSettingsStore::new(&path);
    let mut settings = store.load().await.unwrap();
    settings.overlay.canvases.reverse();
    store.save(&settings).await.unwrap();
    let disk: serde_json::Value = serde_json::from_slice(&std::fs::read(path).unwrap()).unwrap();
    assert_eq!(disk["Overlay"]["Canvases"][0]["Future"]["value"], 2);
    assert_eq!(disk["Overlay"]["Canvases"][1]["Future"]["value"], 1);
}
