use ccs_core::JsonSettingsStore;
use serde_json::json;

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
