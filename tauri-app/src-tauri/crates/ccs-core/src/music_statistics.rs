//! Local Spotify recognition/listening statistics, compatible with the WPF JSON array.
use crate::{
    spotify_states::{atomic_write, read_bytes},
    store::SettingsError,
};
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
};
use tokio::sync::Mutex;
type Result<T> = std::result::Result<T, SettingsError>;

#[derive(Clone, Debug)]
pub struct ListeningTrack {
    pub id: String,
    pub title: String,
    pub artist: String,
    pub album: String,
}
#[derive(Clone, Debug)]
pub struct ListeningSample {
    pub track: Option<ListeningTrack>,
    pub is_playing: bool,
}
#[derive(Clone, Debug, Default, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "PascalCase", default)]
pub struct TrackStatistic {
    pub track_id: String,
    pub title: String,
    pub artist: String,
    pub album: String,
    pub play_count: u64,
    pub listening_seconds: f64,
    pub last_played_at: DateTime<Utc>,
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ArtistStatistic {
    pub artist: String,
    pub play_count: u64,
    pub listening_seconds: f64,
}
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StatisticsSnapshot {
    pub total_plays: u64,
    pub total_listening_seconds: f64,
    pub top_tracks: Vec<TrackStatistic>,
    pub top_artists: Vec<ArtistStatistic>,
}
#[derive(Default)]
struct Timeline {
    active: Option<String>,
    last_sample: Option<DateTime<Utc>>,
}
pub struct MusicStatisticsStore {
    file: PathBuf,
    timeline: Mutex<Timeline>,
}
impl MusicStatisticsStore {
    pub fn new(root: impl AsRef<Path>) -> Self {
        Self {
            file: root
                .as_ref()
                .join("Statistics/spotify-listening-statistics.json"),
            timeline: Mutex::new(Timeline::default()),
        }
    }
    async fn tracks(&self) -> Result<BTreeMap<String, TrackStatistic>> {
        let bytes = match read_bytes(&self.file).await {
            Ok(bytes) => bytes,
            Err(SettingsError::Io(e)) if e.kind() == std::io::ErrorKind::NotFound => {
                return Ok(BTreeMap::new())
            }
            Err(e) => return Err(e),
        };
        let rows: Vec<TrackStatistic> = serde_json::from_slice(&bytes)?;
        let mut tracks = BTreeMap::new();
        for row in rows {
            if row.track_id.trim().is_empty() {
                continue;
            }
            if !row.listening_seconds.is_finite()
                || row.listening_seconds < 0.0
                || row.listening_seconds > 1e12
            {
                return Err(SettingsError::Validation(
                    "Ungültige Spotify-Hörzeit in der Statistikdatei".into(),
                ));
            }
            tracks.insert(row.track_id.clone(), row);
        }
        Ok(tracks)
    }
    async fn save(&self, tracks: &BTreeMap<String, TrackStatistic>) -> Result<()> {
        let mut rows = tracks.values().cloned().collect::<Vec<_>>();
        rows.sort_by(|a, b| a.artist.cmp(&b.artist).then_with(|| a.title.cmp(&b.title)));
        for row in &mut rows {
            let secs = row.listening_seconds as u64;
            row.extra.insert(
                "DisplayText".into(),
                Value::String(format!(
                    "{}× · {} – {} · {:02}:{:02}:{:02}",
                    row.play_count,
                    row.artist,
                    row.title,
                    secs / 3600 % 24,
                    secs / 60 % 60,
                    secs % 60
                )),
            );
        }
        atomic_write(&self.file, &serde_json::to_vec_pretty(&rows)?).await
    }
    pub async fn observe_at(&self, sample: ListeningSample, now: DateTime<Utc>) -> Result<()> {
        let mut timeline = self.timeline.lock().await;
        let mut tracks = self.tracks().await?;
        let elapsed = timeline
            .last_sample
            .map(|at| (now - at).num_milliseconds() as f64 / 1000.0)
            .unwrap_or(0.0)
            .clamp(0.0, 15.0);
        if sample.is_playing {
            if let Some(active) = timeline.active.as_ref().and_then(|id| tracks.get_mut(id)) {
                active.listening_seconds += elapsed;
            }
        }
        let active = if let Some(track) = sample.track.filter(|t| !t.id.trim().is_empty()) {
            let statistic = tracks
                .entry(track.id.clone())
                .or_insert_with(|| TrackStatistic {
                    track_id: track.id.clone(),
                    title: track.title,
                    artist: track.artist,
                    album: track.album,
                    ..Default::default()
                });
            if timeline.active.as_ref() != Some(&track.id) {
                statistic.play_count = statistic.play_count.saturating_add(1);
                statistic.last_played_at = now;
            }
            Some(track.id)
        } else {
            None
        };
        self.save(&tracks).await?;
        timeline.active = active;
        timeline.last_sample = Some(now);
        Ok(())
    }
    pub async fn suspend_at(&self, now: DateTime<Utc>) {
        let mut timeline = self.timeline.lock().await;
        timeline.active = None;
        timeline.last_sample = Some(now);
    }
    pub async fn reset(&self) -> Result<()> {
        let mut timeline = self.timeline.lock().await;
        self.tracks().await?; // A malformed file requires explicit file recovery, not silent data loss.
        self.save(&BTreeMap::new()).await?;
        *timeline = Timeline::default();
        Ok(())
    }
    pub async fn snapshot(&self) -> Result<StatisticsSnapshot> {
        let _timeline = self.timeline.lock().await;
        let tracks = self.tracks().await?;
        let total_plays = tracks
            .values()
            .fold(0u64, |total, row| total.saturating_add(row.play_count));
        let total_listening_seconds = tracks.values().map(|row| row.listening_seconds).sum();
        let mut top_tracks = tracks.values().cloned().collect::<Vec<_>>();
        top_tracks.sort_by(|a, b| {
            b.play_count
                .cmp(&a.play_count)
                .then_with(|| b.listening_seconds.total_cmp(&a.listening_seconds))
                .then_with(|| a.track_id.cmp(&b.track_id))
        });
        top_tracks.truncate(10);
        let mut artists: BTreeMap<String, ArtistStatistic> = BTreeMap::new();
        for row in tracks.values() {
            let artist =
                artists
                    .entry(row.artist.to_lowercase())
                    .or_insert_with(|| ArtistStatistic {
                        artist: row.artist.clone(),
                        play_count: 0,
                        listening_seconds: 0.0,
                    });
            artist.play_count = artist.play_count.saturating_add(row.play_count);
            artist.listening_seconds += row.listening_seconds;
        }
        let mut top_artists = artists.into_values().collect::<Vec<_>>();
        top_artists.sort_by(|a, b| {
            b.play_count
                .cmp(&a.play_count)
                .then_with(|| b.listening_seconds.total_cmp(&a.listening_seconds))
                .then_with(|| a.artist.cmp(&b.artist))
        });
        top_artists.truncate(10);
        Ok(StatisticsSnapshot {
            total_plays,
            total_listening_seconds,
            top_tracks,
            top_artists,
        })
    }
}
