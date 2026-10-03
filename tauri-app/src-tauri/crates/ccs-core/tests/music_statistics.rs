use ccs_core::music_statistics::{ListeningSample, ListeningTrack, MusicStatisticsStore};
use chrono::{Duration, Utc};
use serde_json::{json, Value};

fn sample(id: &str, artist: &str, playing: bool) -> ListeningSample {
    ListeningSample {
        is_playing: playing,
        track: Some(ListeningTrack {
            id: id.into(),
            title: format!("Song {id}"),
            artist: artist.into(),
            album: "Album".into(),
        }),
    }
}
#[tokio::test]
async fn csharp_sampling_counts_changes_caps_elapsed_and_preserves_pause_and_resume_semantics() {
    let root = tempfile::tempdir().unwrap();
    let store = MusicStatisticsStore::new(root.path());
    let now = Utc::now();
    store
        .observe_at(sample("a", "Artist", true), now)
        .await
        .unwrap();
    store
        .observe_at(sample("a", "Artist", true), now + Duration::seconds(10))
        .await
        .unwrap();
    store
        .observe_at(sample("b", "ARTIST", true), now + Duration::seconds(40))
        .await
        .unwrap();
    store
        .observe_at(sample("b", "ARTIST", false), now + Duration::seconds(50))
        .await
        .unwrap();
    store
        .observe_at(sample("b", "ARTIST", true), now + Duration::seconds(55))
        .await
        .unwrap();
    let data = store.snapshot().await.unwrap();
    assert_eq!(data.total_plays, 2);
    assert_eq!(data.total_listening_seconds, 30.0);
    assert_eq!(data.top_tracks[0].track_id, "a");
    assert_eq!(data.top_tracks[0].listening_seconds, 25.0);
    assert_eq!(data.top_artists.len(), 1);
    assert_eq!(data.top_artists[0].play_count, 2);
    assert_eq!(data.top_artists[0].listening_seconds, 30.0);
    let persisted: Value = serde_json::from_slice(
        &std::fs::read(
            root.path()
                .join("Statistics/spotify-listening-statistics.json"),
        )
        .unwrap(),
    )
    .unwrap();
    assert!(persisted.is_array());
    assert!(persisted
        .as_array()
        .unwrap()
        .iter()
        .any(|row| row["TrackId"] == "a"));
    assert!(persisted
        .as_array()
        .unwrap()
        .iter()
        .any(|row| row["TrackId"] == "b"));
    assert_eq!(
        MusicStatisticsStore::new(root.path())
            .snapshot()
            .await
            .unwrap()
            .total_plays,
        2
    );
}
#[tokio::test]
async fn missing_track_breaks_active_identity_and_suspension_does_not_credit_connection_gaps() {
    let root = tempfile::tempdir().unwrap();
    let store = MusicStatisticsStore::new(root.path());
    let now = Utc::now();
    store
        .observe_at(sample("a", "Artist", false), now)
        .await
        .unwrap();
    assert_eq!(store.snapshot().await.unwrap().total_plays, 1); // C# records recognition even when paused.
    store
        .observe_at(
            ListeningSample {
                track: None,
                is_playing: false,
            },
            now + Duration::seconds(2),
        )
        .await
        .unwrap();
    store
        .observe_at(sample("a", "Artist", true), now + Duration::seconds(4))
        .await
        .unwrap();
    store.suspend_at(now + Duration::seconds(5)).await;
    store
        .observe_at(sample("a", "Artist", true), now + Duration::hours(1))
        .await
        .unwrap();
    let data = store.snapshot().await.unwrap();
    assert_eq!(data.total_plays, 3);
    assert_eq!(data.total_listening_seconds, 0.0);
}
#[tokio::test]
async fn csharp_import_keeps_extra_fields_and_returns_top_ten_with_count_then_time_order() {
    let root = tempfile::tempdir().unwrap();
    std::fs::create_dir_all(root.path().join("Statistics")).unwrap();
    let file = root
        .path()
        .join("Statistics/spotify-listening-statistics.json");
    let rows=(0..12).map(|i|json!({"TrackId":format!("id{i}"),"Title":format!("Title{i}"),"Artist":format!("Artist{i}"),"Album":"Album","PlayCount":1,"ListeningSeconds":i as f64,"LastPlayedAt":"2025-01-01T12:00:00+01:00","DisplayText":"legacy","Extra":{"keep":42}})).collect::<Vec<_>>();
    std::fs::write(&file, serde_json::to_vec(&rows).unwrap()).unwrap();
    let store = MusicStatisticsStore::new(root.path());
    let data = store.snapshot().await.unwrap();
    assert_eq!(data.top_tracks.len(), 10);
    assert_eq!(data.top_artists.len(), 10);
    assert_eq!(data.top_tracks[0].track_id, "id11");
    assert_eq!(data.total_plays, 12);
    store
        .observe_at(sample("id0", "Artist0", false), Utc::now())
        .await
        .unwrap();
    let rows: Vec<Value> = serde_json::from_slice(&std::fs::read(&file).unwrap()).unwrap();
    assert!(rows.iter().all(|row| row["Extra"]["keep"] == 42));
    store.reset().await.unwrap();
    assert_eq!(store.snapshot().await.unwrap().total_plays, 0);
    assert_eq!(
        MusicStatisticsStore::new(root.path())
            .snapshot()
            .await
            .unwrap()
            .top_tracks
            .len(),
        0
    );
}
#[tokio::test]
async fn corrupt_data_and_failed_writes_remain_visible_without_replacing_existing_statistics() {
    let root = tempfile::tempdir().unwrap();
    std::fs::create_dir_all(root.path().join("Statistics")).unwrap();
    let file = root
        .path()
        .join("Statistics/spotify-listening-statistics.json");
    std::fs::write(&file, "broken json").unwrap();
    let store = MusicStatisticsStore::new(root.path());
    assert!(store
        .observe_at(sample("a", "Artist", true), Utc::now())
        .await
        .is_err());
    assert!(store.snapshot().await.is_err());
    assert!(store.reset().await.is_err());
    assert_eq!(std::fs::read_to_string(&file).unwrap(), "broken json");
    let bad_root = root.path().join("file-as-directory");
    std::fs::write(&bad_root, "keep").unwrap();
    assert!(MusicStatisticsStore::new(&bad_root)
        .observe_at(sample("a", "Artist", true), Utc::now())
        .await
        .is_err());
    assert_eq!(std::fs::read_to_string(bad_root).unwrap(), "keep");
}
