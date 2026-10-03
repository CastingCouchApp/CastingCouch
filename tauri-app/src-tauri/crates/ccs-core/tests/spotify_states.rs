use ccs_core::spotify_states::{SavedPlaybackState, SpotifyStateStore};
use chrono::{Duration, Utc};
use serde_json::{json, Value};

fn state(at: chrono::DateTime<Utc>) -> SavedPlaybackState {
    serde_json::from_value(json!({"ContextUri":"spotify:playlist:abc","Track":{"Id":"track","Uri":"spotify:track:track","Name":"Song","Artist":"Artist","Custom":42},"ProgressMs":1234,"VolumePercent":64,"ShuffleEnabled":true,"RepeatMode":"context","WasPlaying":false,"SavedAtUtc":at,"Custom":{"keep":true}})).unwrap()
}
#[tokio::test]
async fn saved_groups_are_case_insensitive_persistent_and_expire_only_after_ttl() {
    let root = tempfile::tempdir().unwrap();
    let store = SpotifyStateStore::new(root.path());
    let now = Utc::now();
    store
        .save("Intro", state(now - Duration::minutes(180)))
        .await
        .unwrap();
    store
        .save("INTRO", state(now - Duration::minutes(180)))
        .await
        .unwrap();
    let loaded = SpotifyStateStore::new(root.path())
        .snapshot()
        .await
        .unwrap();
    assert_eq!(loaded["states"].as_object().unwrap().len(), 1);
    assert_eq!(loaded["states"]["Intro"]["Custom"]["keep"], true);
    assert_eq!(loaded["history"]["SavedCount"], 2);
    assert_eq!(store.cleanup(180, now).await.unwrap(), 0);
    assert_eq!(
        store
            .cleanup(180, now + Duration::seconds(1))
            .await
            .unwrap(),
        1
    );
    assert_eq!(
        store.snapshot().await.unwrap()["history"]["CleanupCount"],
        1
    );
}
#[tokio::test]
async fn csharp_history_import_keeps_unknown_fields_and_edits_notes_favorites_and_filters() {
    let root = tempfile::tempdir().unwrap();
    std::fs::write(root.path().join("spotify-saved-state-history.json"),serde_json::to_vec(&json!({"FormatVersion":1,"Entries":["10:00:00 · Intro: 'Song' gespeichert","10:01:00 · Intro: Wiedergabe wiederhergestellt"],"SavedCount":4,"RestoredCount":2,"FavoriteEntries":["10:00:00 · Intro: 'Song' gespeichert"],"Notes":{"10:00:00 · Intro: 'Song' gespeichert":"Alte Notiz"},"SearchText":"Song","ActionFilterIndex":1,"SortIndex":2,"FavoritesOnly":true,"Custom":{"keep":42}})).unwrap()).unwrap();
    let store = SpotifyStateStore::new(root.path());
    let entry = "10:01:00 · Intro: Wiedergabe wiederhergestellt".to_string();
    store
        .edit_history(&[entry.clone()], Some(true), Some("Neue Notiz"), false)
        .await
        .unwrap();
    store.set_filters(json!({"SearchText":"Neue Notiz","ActionFilterIndex":2,"SortIndex":0,"FavoritesOnly":true})).await.unwrap();
    let doc = store.snapshot().await.unwrap();
    assert_eq!(doc["history"]["Custom"]["keep"], 42);
    assert_eq!(doc["visibleHistory"], json!([entry]));
    let export = String::from_utf8(
        store
            .export_history(Some(&[entry.clone()]), false)
            .await
            .unwrap(),
    )
    .unwrap();
    let value: Value = serde_json::from_str(&export).unwrap();
    assert_eq!(value["FormatVersion"], 2);
    assert_eq!(value["Notes"][&entry], "Neue Notiz");
    let csv = String::from_utf8(
        store
            .export_history(Some(&[entry.clone()]), true)
            .await
            .unwrap(),
    )
    .unwrap();
    assert!(csv.starts_with('\u{feff}'));
    assert!(csv.contains("Zeit;Aktion;Favorit;Notiz"));
    assert!(csv.contains("Neue Notiz"));
    store
        .edit_history(&[entry.clone()], None, None, true)
        .await
        .unwrap();
    let doc = store.snapshot().await.unwrap();
    assert!(doc["history"]["Notes"].get(&entry).is_none());
}
#[tokio::test]
async fn backup_preview_and_selective_restore_preserve_current_metadata_and_reject_stale_original()
{
    let root = tempfile::tempdir().unwrap();
    let store = SpotifyStateStore::new(root.path());
    store.save("First", state(Utc::now())).await.unwrap();
    let id = store.backup().await.unwrap();
    store.save("Second", state(Utc::now())).await.unwrap();
    let preview = store.preview_backup(&id).await.unwrap();
    assert_eq!(preview["removed"].as_array().unwrap().len(), 1);
    store
        .set_filters(json!({"SearchText":"current"}))
        .await
        .unwrap();
    assert!(store
        .restore_history(&id, json!({"Entries":true}), &preview["original"])
        .await
        .is_err());
    let preview = store.preview_backup(&id).await.unwrap();
    store
        .restore_history(
            &id,
            json!({"Entries":true,"Counters":true,"MergeEntries":false}),
            &preview["original"],
        )
        .await
        .unwrap();
    let doc = store.snapshot().await.unwrap();
    assert_eq!(doc["history"]["Entries"].as_array().unwrap().len(), 1);
    assert_eq!(doc["history"]["SearchText"], "current");
    assert_eq!(doc["history"]["SavedCount"], 1);
    assert_eq!(doc["states"].as_object().unwrap().len(), 2);
    assert!(store.preview_backup("../settings.json").await.is_err());
    for _ in 0..12 {
        store.backup().await.unwrap();
    }
    assert_eq!(
        store.snapshot().await.unwrap()["backups"]
            .as_array()
            .unwrap()
            .len(),
        10
    );
}
#[tokio::test]
async fn restore_profiles_import_is_reviewable_conflict_checked_and_supports_copy_and_skip() {
    let root = tempfile::tempdir().unwrap();
    let store = SpotifyStateStore::new(root.path());
    store
        .save_profile(json!({"Name":"Custom","Entries":true,"MergeEntries":true,"Extra":42}))
        .await
        .unwrap();
    let text=json!({"Format":"CreatorControlSuite.SpotifyHistoryRestoreProfiles","Version":1,"Profiles":[{"Name":"CUSTOM","Entries":false,"Notes":true,"Extra":84},{"Name":"New","Entries":true}]}).to_string();
    let preview = store.preview_profiles_import(&text).await.unwrap();
    assert_eq!(preview["profiles"][0]["status"], "changed");
    store
        .import_profiles(
            &preview["profiles"],
            &["copy".into(), "skip".into()],
            &preview["original"],
        )
        .await
        .unwrap();
    let snapshot = store.snapshot().await.unwrap();
    let profiles = snapshot["profiles"].as_array().unwrap();
    assert!(profiles
        .iter()
        .any(|p| p["Name"] == "CUSTOM 2" && p["Extra"] == 84));
    assert_eq!(profiles.len(), 5);
    assert!(store
        .import_profiles(
            &preview["profiles"],
            &["overwrite".into(), "skip".into()],
            &preview["original"]
        )
        .await
        .is_err());
    assert!(store
        .delete_profile("Nur Verlauf zusammenführen")
        .await
        .is_err());
    store.delete_profile("custom").await.unwrap();
    let export = store.export_profiles().await.unwrap();
    assert_eq!(
        export["Format"],
        "CreatorControlSuite.SpotifyHistoryRestoreProfiles"
    );
    assert_eq!(export["Profiles"].as_array().unwrap().len(), 1);
}
#[tokio::test]
async fn malformed_or_future_documents_never_get_overwritten_by_mutation() {
    let root = tempfile::tempdir().unwrap();
    let file = root.path().join("spotify-saved-state-history.json");
    for text in ["broken json", "{\"FormatVersion\":9,\"Entries\":[]}"] {
        std::fs::write(&file, text).unwrap();
        let store = SpotifyStateStore::new(root.path());
        assert!(store.save("Intro", state(Utc::now())).await.is_err());
        assert_eq!(std::fs::read_to_string(&file).unwrap(), text);
        assert!(!root.path().join("spotify-saved-states.json").exists());
    }
    let store = SpotifyStateStore::new(root.path());
    assert!(store
        .preview_profiles_import("{\"Version\":8,\"Profiles\":[]}")
        .await
        .is_err());
}

#[tokio::test]
async fn german_group_and_profile_names_are_case_insensitive_and_newer_captures_survive_restore() {
    let root = tempfile::tempdir().unwrap();
    let store = SpotifyStateStore::new(root.path());
    let first = state(Utc::now());
    store.save("Übergang", first.clone()).await.unwrap();
    let mut newer = first.clone();
    newer.progress_ms = 9000;
    store.save("ÜBERGANG", newer.clone()).await.unwrap();
    assert_eq!(
        store.snapshot().await.unwrap()["states"]
            .as_object()
            .unwrap()
            .len(),
        1
    );
    store.consume("übergang", &first).await.unwrap();
    assert_eq!(store.get("übergang").await.unwrap().progress_ms, 9000);
    store
        .save_profile(json!({"Name":"Änderung","Entries":true}))
        .await
        .unwrap();
    store
        .save_profile(json!({"Name":"änderung","Notes":true}))
        .await
        .unwrap();
    assert_eq!(
        store.export_profiles().await.unwrap()["Profiles"]
            .as_array()
            .unwrap()
            .len(),
        1
    );
    store.delete_profile("ÄNDERUNG").await.unwrap();
    assert!(store.export_profiles().await.unwrap()["Profiles"]
        .as_array()
        .unwrap()
        .is_empty());
}

#[tokio::test]
async fn music_data_folders_resolve_to_the_owned_data_root_and_are_created() {
    let root = tempfile::tempdir().unwrap();
    let store = SpotifyStateStore::new(root.path().join("MusicData"));
    assert_eq!(
        store.prepare_folder(false).await.unwrap(),
        root.path().join("MusicData")
    );
    let backup = store.prepare_folder(true).await.unwrap();
    assert_eq!(backup, root.path().join("MusicData/Backups/SpotifyHistory"));
    assert!(backup.is_dir());
}
