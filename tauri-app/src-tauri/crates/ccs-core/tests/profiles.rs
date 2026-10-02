use ccs_core::{profiles::ProfileStore, JsonSettingsStore};
use serde_json::json;

#[tokio::test]
async fn file_export_import_replacement_and_failures_keep_source_data_intact() {
    let dir = tempfile::tempdir().unwrap();
    let settings = JsonSettingsStore::new(dir.path().join("settings.json"));
    let store = ProfileStore::new(dir.path().join("Profiles"));
    let profile = store
        .create("Studio", "", settings.read_value().await.unwrap())
        .await
        .unwrap();
    let exported = dir.path().join("Studio.ccsprofile");
    store.export_to(&profile.id, &exported).await.unwrap();
    store.rename(&profile.id, "Renamed", "").await.unwrap();
    store.export_to(&profile.id, &exported).await.unwrap();
    let bytes = std::fs::read(&exported).unwrap();
    let imported = store.import_from(&exported).await.unwrap();
    assert_eq!(imported.name, "Renamed (Import)");
    assert_eq!(std::fs::read(&exported).unwrap(), bytes);
    let current = std::fs::read(settings.path()).unwrap();
    assert!(store.export_to(&profile.id, settings.path()).await.is_err());
    assert_eq!(std::fs::read(settings.path()).unwrap(), current);
    let blocked = dir.path().join("blocked.ccsprofile");
    std::fs::create_dir(&blocked).unwrap();
    assert!(store.export_to(&profile.id, &blocked).await.is_err());
    assert_eq!(store.load(&profile.id).await.unwrap().name, "Renamed");
    let oversized = dir.path().join("large.ccsprofile");
    let file = std::fs::File::create(&oversized).unwrap();
    file.set_len(16 * 1024 * 1024 + 1).unwrap();
    assert!(store.import_from(&oversized).await.is_err());
    assert_eq!(store.list().await.unwrap().profiles.len(), 2);
}

#[tokio::test]
async fn profiles_roundtrip_restart_export_import_and_preserve_csharp_fields() {
    let dir = tempfile::tempdir().unwrap();
    let settings = JsonSettingsStore::new(dir.path().join("settings.json"));
    let mut value = settings.read_value().await.unwrap();
    value["Branding"]["DisplayName"] = json!("Studio");
    value["Obs"]["CustomNested"] = json!({"keep":12});
    value["StreamerBot"]["Password"] = json!("private");
    let store = ProfileStore::new(dir.path().join("Profiles"));
    let created = store
        .create(" Studio ", " Description ", value.clone())
        .await
        .unwrap();
    assert_eq!(created.name, "Studio");
    assert_eq!(created.settings["StreamerBot"]["Password"], "");
    assert_eq!(value["StreamerBot"]["Password"], "private");
    let restarted = ProfileStore::new(dir.path().join("Profiles"));
    assert_eq!(restarted.list().await.unwrap().profiles[0].id, created.id);
    let exported = restarted.export(&created.id).await.unwrap();
    let raw: serde_json::Value = serde_json::from_str(&exported).unwrap();
    assert_eq!(raw["Settings"]["Obs"]["CustomNested"]["keep"], 12);
    assert_eq!(raw["Settings"]["StreamerBot"]["Password"], "");
    let imported = restarted.import(&exported).await.unwrap();
    assert_ne!(imported.id, created.id);
    assert_eq!(imported.name, "Studio (Import)");
    let mut current = value.clone();
    current["StreamerBot"]["Password"] = json!("current-secret");
    let apply = restarted
        .prepare_apply(&imported.id, &current)
        .await
        .unwrap();
    assert_eq!(apply["StreamerBot"]["Password"], "current-secret");
    assert_eq!(apply["Obs"]["CustomNested"]["keep"], 12);
    restarted.delete(&created.id).await.unwrap();
    assert_eq!(restarted.list().await.unwrap().profiles.len(), 1);
}

#[tokio::test]
async fn invalid_profiles_are_reported_and_cannot_escape_or_overwrite_existing_records() {
    let dir = tempfile::tempdir().unwrap();
    let settings = JsonSettingsStore::new(dir.path().join("settings.json"));
    let store = ProfileStore::new(dir.path().join("Profiles"));
    let created = store
        .create("Studio", "", settings.read_value().await.unwrap())
        .await
        .unwrap();
    std::fs::write(dir.path().join("Profiles/broken.json"), "bad").unwrap();
    let list = store.list().await.unwrap();
    assert_eq!(list.profiles.len(), 1);
    assert_eq!(list.warnings.len(), 1);
    assert!(store.load("../settings").await.is_err());
    assert!(store.delete("../settings").await.is_err());
    assert!(store.import("{}").await.is_err());
    let future = store
        .export(&created.id)
        .await
        .unwrap()
        .replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 999");
    assert!(store.import(&future).await.is_err());
    assert_eq!(store.load(&created.id).await.unwrap().name, "Studio");
    let edited = store.rename(&created.id, "Changed", "About").await.unwrap();
    assert_eq!(edited.created_at, created.created_at);
    assert_eq!(edited.settings, created.settings);
}

#[tokio::test]
async fn imported_wpf_file_keeps_metadata_and_normalizes_settings_without_mutating_source() {
    let dir = tempfile::tempdir().unwrap();
    let store = ProfileStore::new(dir.path().join("Profiles"));
    let content=json!({"Id":"legacy-id","Name":"CSharp","Description":"Imported","CreatedAt":"2026-01-01T00:00:00+00:00","UpdatedAt":"2026-01-02T00:00:00+00:00","CustomMetadata":{"keep":true},"Settings":{"SchemaVersion":1,"Obs":{"Host":"localhost","Port":4455},"MusicPlayer":{"ProviderId":"ytmusic"},"StreamerBot":{"Password":"external-secret"}}}).to_string();
    let imported = store.import(&content).await.unwrap();
    let current = json!({"StreamerBot":{"Password":"local-secret"}});
    let applied = store.prepare_apply(&imported.id, &current).await.unwrap();
    assert_eq!(applied["MusicPlayer"]["ProviderId"], "ytmusic");
    assert_eq!(applied["Overlay"]["WebServerPort"], 8765);
    assert_eq!(applied["StreamerBot"]["Password"], "local-secret");
    assert_eq!(
        serde_json::from_str::<serde_json::Value>(&store.export(&imported.id).await.unwrap())
            .unwrap()["CustomMetadata"]["keep"],
        true
    );
    assert!(content.contains("external-secret"));
}
