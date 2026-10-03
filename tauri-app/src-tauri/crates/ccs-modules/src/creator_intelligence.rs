//! Creator Intelligence operates on the full compatible journal, independently of
//! the bounded UI feed. Formulae and sampling windows follow the C# implementation.
use crate::stream_history::{checkpoint, StreamHistoryRuntime};
use chrono::{DateTime, Datelike, Duration, FixedOffset, Local, Timelike, Utc};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::{HashMap, HashSet};
use std::{
    fs,
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
};

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct JournalEvent {
    pub timestamp_utc: DateTime<Utc>,
    pub session_id: String,
    #[serde(rename = "Type")]
    pub kind: String,
    pub payload: Option<Value>,
}
macro_rules! model {
    ($name:ident { $($field:ident: $typ:ty),* $(,)? }) => {
        #[derive(Clone, Debug, Serialize, Deserialize)]
        #[serde(rename_all = "PascalCase")]
        pub struct $name { $(pub $field: $typ),* }
    };
}
model!(Summary {
    session_id: String, started_at: DateTime<FixedOffset>, ended_at: DateTime<FixedOffset>,
    title: String, category: String, duration: String, creator_score: i32, peak_viewers: i32,
    average_viewers: f64, retention_percent: f64, chat_messages_per_hour: f64,
    followers_per_hour: f64, chat_messages: usize, followers: usize, distinct_scenes: usize,
    tracks_played: usize, recommendations: Vec<String>,
});
model!(Dashboard {
    lookback_days: i32, session_count: usize, weekly_session_count: usize,
    weekly_average_creator_score: f64, average_creator_score: f64, stream_quality_index: i32,
    engagement_index: i32, growth_index: i32, average_retention_percent: f64,
    average_chat_messages_per_hour: f64, average_followers_per_hour: f64, average_viewers: f64,
    creator_score_trend: f64, viewer_trend_per_stream: f64, best_start_hour: u32, best_day: u32,
    best_category: String, predicted_average_viewers: f64, predicted_creator_score: i32,
    recent_sessions: Vec<Summary>, insights: Vec<String>,
});
model!(ContentRow {
    kind: String,
    name: String,
    occurrences: usize,
    total_minutes: f64,
    average_viewers: f64,
    viewer_delta: f64,
    chat_messages_per_minute: f64,
});
model!(HeatmapCell {
    day: u32,
    hour: u32,
    sample_count: usize,
    average_viewers: f64
});
model!(Content {
    lookback_days: i32, session_count: usize, scenes: Vec<ContentRow>, tracks: Vec<ContentRow>,
    heatmap: Vec<HeatmapCell>, insights: Vec<String>,
});
model!(CorrelationRow {
    event_name: String,
    event_type: String,
    occurrences: usize,
    baseline_viewers: f64,
    viewer_delta5_minutes: f64,
    viewer_delta10_minutes: f64,
});
model!(RaidRow {
    raid_summary: String,
    viewers_before: f64,
    viewers_after5: f64,
    viewers_after10: f64,
    viewers_after30: f64,
    retention30_percent: f64,
});
model!(Correlation {
    lookback_days: i32, session_count: usize, correlations: Vec<CorrelationRow>,
    raids: Vec<RaidRow>, actions: Vec<String>,
});
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Analysis {
    pub latest: Option<Summary>,
    pub dashboard: Dashboard,
    pub content: Content,
    pub correlation: Correlation,
}

fn text(event: &JournalEvent, key: &str) -> String {
    match event.payload.as_ref().and_then(|v| v.get(key)) {
        Some(Value::String(s)) => s.clone(),
        Some(Value::Null) | None => String::new(),
        Some(value) => value.to_string(),
    }
}
fn viewers(event: &JournalEvent) -> i32 {
    event
        .payload
        .as_ref()
        .and_then(|v| v["viewers"].as_i64())
        .and_then(|v| i32::try_from(v).ok())
        .unwrap_or(0)
}
fn mean(values: impl Iterator<Item = f64>) -> f64 {
    let (sum, n) = values.fold((0., 0), |(sum, n), value| (sum + value, n + 1));
    if n == 0 {
        0.
    } else {
        sum / n as f64
    }
}
fn score(value: f64) -> i32 {
    value.clamp(0., 100.).round_ties_even() as i32
}
fn day_name(day: u32) -> &'static str {
    [
        "Sonntag",
        "Montag",
        "Dienstag",
        "Mittwoch",
        "Donnerstag",
        "Freitag",
        "Samstag",
    ][day as usize]
}
fn signed(value: f64) -> String {
    if value > 0. {
        format!("+{value:.1}")
    } else {
        format!("{value:.1}")
    }
}
fn timespan(duration: Duration) -> String {
    let sign = if duration < Duration::zero() { "-" } else { "" };
    let seconds = duration.num_seconds().unsigned_abs();
    let ticks = duration.subsec_nanos().unsigned_abs() / 100;
    let days = seconds / 86400;
    let prefix = if days > 0 {
        format!("{days}.")
    } else {
        String::new()
    };
    let fraction = if ticks > 0 {
        format!(".{ticks:07}")
    } else {
        String::new()
    };
    format!(
        "{sign}{prefix}{:02}:{:02}:{:02}{fraction}",
        seconds / 3600 % 24,
        seconds / 60 % 60,
        seconds % 60
    )
}
fn summary(
    events: &[&JournalEvent],
    local: &impl Fn(DateTime<Utc>) -> DateTime<FixedOffset>,
) -> Summary {
    let start = events
        .iter()
        .find(|e| e.kind == "session.started")
        .unwrap_or(&events[0]);
    let end = events
        .iter()
        .rev()
        .find(|e| e.kind == "session.ended")
        .unwrap_or(events.last().unwrap());
    let duration = end.timestamp_utc - start.timestamp_utc;
    let hours = (duration.as_seconds_f64() / 3600.).max(1. / 60.);
    let samples: Vec<_> = events
        .iter()
        .filter(|e| e.kind == "twitch.viewer.sample" && e.payload.is_some())
        .map(|e| viewers(e) as f64)
        .collect();
    let chats = events
        .iter()
        .filter(|e| e.kind == "twitch.chat.message")
        .count();
    let follows = events.iter().filter(|e| e.kind == "twitch.follow").count();
    let scenes = events
        .iter()
        .filter(|e| e.kind == "obs.scene.changed")
        .map(|e| text(e, "scene").to_lowercase())
        .filter(|s| !s.trim().is_empty())
        .collect::<HashSet<_>>()
        .len();
    let third = (samples.len() / 3).max(1);
    let first = mean(samples.iter().take(third).copied());
    let last = mean(samples.iter().rev().take(third).copied());
    let retention = if first <= 0. {
        100.
    } else {
        (last / first * 100.).clamp(0., 200.)
    };
    let engagement = chats as f64 / hours;
    let growth = follows as f64 / hours;
    let mut recommendations = vec![];
    if samples.len() < 3 {
        recommendations.push("Mehr Zuschauer-Messpunkte sammeln; für belastbare Trends werden mindestens drei Live-Samples benötigt.".into());
    }
    if retention < 75. {
        recommendations.push("Die Zuschauerbindung fällt zum Streamende deutlich ab. Plane vor dem typischen Einbruch einen Szenen-, Kategorie- oder Content-Wechsel.".into());
    } else if retention > 110. {
        recommendations.push("Die Zuschauerzahl wächst im letzten Streamdrittel. Der dortige Inhalt sollte künftig früher oder häufiger eingesetzt werden.".into());
    }
    if engagement < 10. {
        recommendations.push("Die Chataktivität ist niedrig. Direkte Fragen, Abstimmungen oder Channel-Point-Aktionen können das Engagement erhöhen.".into());
    }
    if scenes <= 1 {
        recommendations.push("Es wurde kaum zwischen OBS-Szenen gewechselt. Mehr visuelle Abwechslung kann längere Streams strukturieren.".into());
    }
    if recommendations.is_empty() {
        recommendations.push("Der Stream zeigt stabile Kennzahlen. Vergleiche als Nächstes Kategorien, Startzeiten und Szenen über mehrere Sessions.".into());
    }
    Summary {
        session_id: start.session_id.clone(),
        started_at: local(start.timestamp_utc),
        ended_at: local(end.timestamp_utc),
        title: text(start, "title"),
        category: text(start, "category"),
        duration: timespan(duration),
        creator_score: score(
            retention * 0.35
                + engagement.min(120.) / 120. * 35.
                + growth.min(10.) / 10. * 20.
                + samples.len().min(20) as f64 / 20. * 10.,
        ),
        peak_viewers: samples.iter().copied().reduce(f64::max).unwrap_or(0.) as i32,
        average_viewers: mean(samples.iter().copied()),
        retention_percent: retention,
        chat_messages_per_hour: engagement,
        followers_per_hour: growth,
        chat_messages: chats,
        followers: follows,
        distinct_scenes: scenes,
        tracks_played: events
            .iter()
            .filter(|e| e.kind == "spotify.track.changed")
            .count(),
        recommendations,
    }
}
fn trend(values: &[f64]) -> f64 {
    let n = values.len() as f64;
    if n < 2. {
        return 0.;
    }
    let x = (n - 1.) * n / 2.;
    let xy: f64 = values.iter().enumerate().map(|(i, v)| i as f64 * v).sum();
    let xx: f64 = (0..values.len()).map(|i| (i * i) as f64).sum();
    (n * xy - x * values.iter().sum::<f64>()) / (n * xx - x * x)
}
// Stable grouping preserves the C# tie order; hash iteration must not change winners.
fn groups<T, K: Eq>(items: impl Iterator<Item = T>, key: impl Fn(&T) -> K) -> Vec<Vec<T>> {
    let mut groups: Vec<(K, Vec<T>)> = vec![];
    for item in items {
        let k = key(&item);
        if let Some((_, group)) = groups.iter_mut().find(|(existing, _)| *existing == k) {
            group.push(item);
        } else {
            groups.push((k, vec![item]));
        }
    }
    groups.into_iter().map(|(_, group)| group).collect()
}
fn dashboard(sessions: &[Summary], days: i32, now: DateTime<Utc>) -> Dashboard {
    let mut result = Dashboard {
        lookback_days: days,
        session_count: sessions.len(),
        weekly_session_count: 0,
        weekly_average_creator_score: 0.,
        average_creator_score: 0.,
        stream_quality_index: 0,
        engagement_index: 0,
        growth_index: 0,
        average_retention_percent: 0.,
        average_chat_messages_per_hour: 0.,
        average_followers_per_hour: 0.,
        average_viewers: 0.,
        creator_score_trend: 0.,
        viewer_trend_per_stream: 0.,
        best_start_hour: 0,
        best_day: 1,
        best_category: "–".into(),
        predicted_average_viewers: 0.,
        predicted_creator_score: 0,
        recent_sessions: vec![],
        insights: vec![],
    };
    if sessions.is_empty() {
        result
            .insights
            .push("Noch keine vollständigen Sessions im gewählten Zeitraum vorhanden.".into());
        return result;
    }
    let recent = &sessions[sessions.len().saturating_sub(5)..];
    let previous = &sessions[sessions.len().saturating_sub(10)..sessions.len().saturating_sub(5)];
    let weekly: Vec<_> = sessions
        .iter()
        .filter(|s| s.started_at >= now - Duration::days(7))
        .collect();
    result.weekly_session_count = weekly.len();
    result.weekly_average_creator_score = mean(weekly.iter().map(|s| s.creator_score as f64));
    result.average_creator_score = mean(sessions.iter().map(|s| s.creator_score as f64));
    result.average_retention_percent = mean(sessions.iter().map(|s| s.retention_percent));
    result.average_chat_messages_per_hour = mean(sessions.iter().map(|s| s.chat_messages_per_hour));
    result.average_followers_per_hour = mean(sessions.iter().map(|s| s.followers_per_hour));
    result.average_viewers = mean(sessions.iter().map(|s| s.average_viewers));
    let recent_score = mean(recent.iter().map(|s| s.creator_score as f64));
    result.creator_score_trend = if previous.is_empty() {
        0.
    } else {
        recent_score - mean(previous.iter().map(|s| s.creator_score as f64))
    };
    result.viewer_trend_per_stream =
        trend(&recent.iter().map(|s| s.average_viewers).collect::<Vec<_>>());
    let best_time = |key: fn(&Summary) -> u32| {
        let mut rows = groups(sessions.iter(), |s| key(s));
        rows.sort_by(|a, b| {
            mean(b.iter().map(|s| s.creator_score as f64))
                .total_cmp(&mean(a.iter().map(|s| s.creator_score as f64)))
                .then_with(|| b.len().cmp(&a.len()))
        });
        key(rows[0][0])
    };
    result.best_start_hour = best_time(|s| s.started_at.hour());
    result.best_day = best_time(|s| s.started_at.weekday().num_days_from_sunday());
    let mut categories = groups(
        sessions.iter().filter(|s| !s.category.trim().is_empty()),
        |s| s.category.to_lowercase(),
    );
    categories.sort_by(|a, b| {
        mean(b.iter().map(|s| s.creator_score as f64))
            .total_cmp(&mean(a.iter().map(|s| s.creator_score as f64)))
            .then_with(|| {
                mean(b.iter().map(|s| s.average_viewers))
                    .total_cmp(&mean(a.iter().map(|s| s.average_viewers)))
            })
    });
    if let Some(best) = categories.first() {
        result.best_category = best[0].category.clone();
    }
    result.predicted_average_viewers =
        (mean(recent.iter().map(|s| s.average_viewers)) + result.viewer_trend_per_stream).max(0.);
    result.predicted_creator_score = score(recent_score + result.creator_score_trend * 0.35);
    result.stream_quality_index = score(
        result.average_creator_score * 0.55
            + result.average_retention_percent.min(120.) / 120. * 45.,
    );
    result.engagement_index = score(
        result.average_chat_messages_per_hour.min(120.) / 120. * 70.
            + result.average_followers_per_hour.min(10.) / 10. * 30.,
    );
    result.growth_index = score(
        result.average_followers_per_hour.min(10.) / 10. * 65.
            + result.viewer_trend_per_stream.clamp(0., 10.) / 10. * 35.,
    );
    result.recent_sessions = sessions.iter().rev().take(12).cloned().collect();
    result.insights.push(if result.creator_score_trend >= 2. { format!("Der Creator Score steigt aktuell um {:.1} Punkte gegenüber dem vorherigen Vergleichszeitraum.", result.creator_score_trend) }
        else if result.creator_score_trend <= -2. { format!("Der Creator Score liegt aktuell {:.1} Punkte unter dem vorherigen Vergleichszeitraum.", -result.creator_score_trend) }
        else { "Der Creator Score ist im Vergleichszeitraum weitgehend stabil.".into() });
    result.insights.push(format!(
        "Die stärkste Startzeit liegt aktuell bei etwa {:02}:00 Uhr ({}).",
        result.best_start_hour,
        day_name(result.best_day)
    ));
    if !categories.is_empty() {
        result.insights.push(format!(
            "Die Kategorie „{}“ erzielt derzeit die beste Kombination aus Score und Zuschauerzahl.",
            result.best_category
        ));
    }
    if result.average_retention_percent < 80. {
        result.insights.push("Die durchschnittliche Zuschauerbindung ist ausbaufähig. Plane den stärksten Inhalt vor dem typischen Rückgang ein.".into());
    }
    if result.average_chat_messages_per_hour < 12. {
        result.insights.push(
            "Mehr direkte Chat-Interaktion könnte den Engagement-Index deutlich verbessern.".into(),
        );
    }
    if sessions.len() < 5 {
        result.insights.push("Für belastbarere Prognosen sollten mindestens fünf vollständige Sessions aufgezeichnet werden.".into());
    }
    result
}
fn music_name(event: &JournalEvent) -> String {
    // Both historical track/name and the actual C# + native writer's title are accepted.
    let title = ["track", "name", "title"]
        .iter()
        .map(|key| text(event, key))
        .find(|s| !s.trim().is_empty())
        .unwrap_or_default();
    let artist = text(event, "artist");
    if artist.trim().is_empty() {
        title
    } else {
        format!("{title} – {artist}")
    }
}
fn segments(events: &[&JournalEvent], kind: &str) -> Vec<ContentRow> {
    let changes: Vec<_> = events.iter().filter(|e| e.kind == kind).collect();
    let mut rows = vec![];
    for (index, change) in changes.iter().enumerate() {
        let name = if kind == "obs.scene.changed" {
            text(change, "scene")
        } else {
            music_name(change)
        };
        if name.trim().is_empty() {
            continue;
        }
        let end = changes
            .get(index + 1)
            .map_or(events.last().unwrap().timestamp_utc, |e| e.timestamp_utc);
        let range: Vec<_> = events
            .iter()
            .filter(|e| e.timestamp_utc >= change.timestamp_utc && e.timestamp_utc < end)
            .collect();
        let samples: Vec<_> = range
            .iter()
            .filter(|e| e.kind == "twitch.viewer.sample" && e.payload.is_some())
            .map(|e| viewers(e) as f64)
            .collect();
        if samples.is_empty() {
            continue;
        }
        let minutes = ((end - change.timestamp_utc).as_seconds_f64() / 60.).max(0.1);
        rows.push(ContentRow {
            kind: if kind == "obs.scene.changed" {
                "OBS-Szene"
            } else {
                "Spotify-Titel"
            }
            .into(),
            name,
            occurrences: 1,
            total_minutes: minutes,
            average_viewers: mean(samples.iter().copied()),
            viewer_delta: samples.last().unwrap() - samples[0],
            chat_messages_per_minute: range
                .iter()
                .filter(|e| e.kind == "twitch.chat.message")
                .count() as f64
                / minutes,
        });
    }
    rows
}
fn aggregate_content(rows: Vec<ContentRow>) -> Vec<ContentRow> {
    let mut result: Vec<_> = groups(rows.into_iter(), |r| {
        (r.kind.to_lowercase(), r.name.to_lowercase())
    })
    .into_iter()
    .map(|g| ContentRow {
        kind: g[0].kind.clone(),
        name: g[0].name.clone(),
        occurrences: g.iter().map(|r| r.occurrences).sum(),
        total_minutes: g.iter().map(|r| r.total_minutes).sum(),
        average_viewers: mean(g.iter().map(|r| r.average_viewers)),
        viewer_delta: mean(g.iter().map(|r| r.viewer_delta)),
        chat_messages_per_minute: mean(g.iter().map(|r| r.chat_messages_per_minute)),
    })
    .collect();
    result.sort_by(|a, b| {
        b.viewer_delta
            .total_cmp(&a.viewer_delta)
            .then_with(|| b.average_viewers.total_cmp(&a.average_viewers))
            .then_with(|| b.occurrences.cmp(&a.occurrences))
    });
    result.truncate(12);
    result
}
fn content(
    sessions: &[Vec<&JournalEvent>],
    days: i32,
    local: &impl Fn(DateTime<Utc>) -> DateTime<FixedOffset>,
) -> Content {
    let mut result = Content {
        lookback_days: days,
        session_count: sessions.len(),
        scenes: vec![],
        tracks: vec![],
        heatmap: vec![],
        insights: vec![],
    };
    if sessions.is_empty() {
        result
            .insights
            .push("Noch keine vollständigen Sessions für die Inhaltsanalyse vorhanden.".into());
        return result;
    }
    result.scenes = aggregate_content(
        sessions
            .iter()
            .flat_map(|s| segments(s, "obs.scene.changed"))
            .collect(),
    );
    result.tracks = aggregate_content(
        sessions
            .iter()
            .flat_map(|s| segments(s, "spotify.track.changed"))
            .collect(),
    );
    let samples = sessions
        .iter()
        .flatten()
        .filter(|e| e.kind == "twitch.viewer.sample" && e.payload.is_some());
    result.heatmap = groups(samples, |e| {
        let at = local(e.timestamp_utc);
        (at.weekday(), at.hour())
    })
    .iter()
    .map(|g| {
        let at = local(g[0].timestamp_utc);
        HeatmapCell {
            day: at.weekday().num_days_from_sunday(),
            hour: at.hour(),
            sample_count: g.len(),
            average_viewers: mean(g.iter().map(|e| viewers(e) as f64)),
        }
    })
    .collect();
    result.heatmap.sort_by(|a, b| {
        b.average_viewers
            .total_cmp(&a.average_viewers)
            .then_with(|| b.sample_count.cmp(&a.sample_count))
    });
    result.heatmap.truncate(18);
    if let Some(row) = result.scenes.first() {
        result.insights.push(format!("Die Szene „{}“ erzielt aktuell die stärkste Zuschauerentwicklung ({} Zuschauer je Einsatz).", row.name, signed(row.viewer_delta)));
    }
    if let Some(row) = result
        .scenes
        .iter()
        .filter(|r| r.occurrences >= 2)
        .min_by(|a, b| a.viewer_delta.total_cmp(&b.viewer_delta))
    {
        if row.viewer_delta < -1. {
            result.insights.push(format!("Bei „{}“ sinkt die Zuschauerzahl im Mittel um {:.1}. Prüfe Länge, Inhalt und Übergang.", row.name, -row.viewer_delta));
        }
    }
    if let Some(row) = result.tracks.first().filter(|r| r.viewer_delta > 0.) {
        result.insights.push(format!(
            "Der Titel „{}“ war bisher mit der besten Zuschauerentwicklung verbunden.",
            row.name
        ));
    }
    if let Some(row) = result.heatmap.first() {
        result.insights.push(format!(
            "Das stärkste gemessene Zeitfenster ist {} um {:02}:00 Uhr mit Ø {:.1} Zuschauern.",
            day_name(row.day),
            row.hour,
            row.average_viewers
        ));
    }
    if result.scenes.is_empty() {
        result.insights.push("Für die Szenenanalyse müssen OBS-Szenenwechsel während vollständiger Sessions aufgezeichnet werden.".into());
    }
    if result.tracks.is_empty() {
        result.insights.push("Für die Songanalyse müssen Spotify-Titelwechsel während vollständiger Sessions aufgezeichnet werden.".into());
    }
    result
}
fn nearest(samples: &[&JournalEvent], target: DateTime<Utc>, before: bool) -> Option<f64> {
    let selected = if before {
        samples.iter().rev().find(|e| e.timestamp_utc <= target)
    } else {
        samples.iter().find(|e| e.timestamp_utc >= target)
    }?;
    if (selected.timestamp_utc - target).abs() > Duration::minutes(12) {
        None
    } else {
        Some(viewers(selected) as f64)
    }
}
fn correlations(sessions: &[Vec<&JournalEvent>], days: i32) -> Correlation {
    let mut result = Correlation {
        lookback_days: days,
        session_count: sessions.len(),
        correlations: vec![],
        raids: vec![],
        actions: vec![],
    };
    if sessions.is_empty() {
        result.actions.push(
            "Noch keine vollständigen Sessions für die Ereigniskorrelation vorhanden.".into(),
        );
        return result;
    }
    let mut rows = vec![];
    let mut raids = vec![];
    for events in sessions {
        let samples: Vec<_> = events
            .iter()
            .filter(|e| e.kind == "twitch.viewer.sample")
            .copied()
            .collect();
        for event in events {
            let name = match event.kind.as_str() {
                "obs.scene.changed" => format!("OBS-Szene: {}", text(event, "scene")),
                "spotify.track.changed" => format!("Spotify: {}", music_name(event)),
                "session.note" => format!("Notiz: {}", text(event, "note")),
                "twitch.event" => text(event, "summary"),
                _ => continue,
            };
            if name.trim().is_empty() {
                continue;
            }
            let Some(before) = nearest(&samples, event.timestamp_utc, true) else {
                continue;
            };
            let Some(after5) = nearest(&samples, event.timestamp_utc + Duration::minutes(5), false)
            else {
                continue;
            };
            let after10 = nearest(&samples, event.timestamp_utc + Duration::minutes(10), false);
            rows.push(CorrelationRow {
                event_name: name,
                event_type: event.kind.clone(),
                occurrences: 1,
                baseline_viewers: before,
                viewer_delta5_minutes: after5 - before,
                viewer_delta10_minutes: after10.map_or(0., |v| v - before),
            });
            if event.kind == "twitch.event"
                && [text(event, "type"), text(event, "summary")]
                    .iter()
                    .any(|s| s.to_lowercase().contains("raid"))
            {
                let after30 = nearest(&samples, event.timestamp_utc + Duration::minutes(30), false)
                    .or(after10)
                    .unwrap_or(after5);
                raids.push(RaidRow {
                    raid_summary: text(event, "summary"),
                    viewers_before: before,
                    viewers_after5: after5,
                    viewers_after10: after10.unwrap_or(after5),
                    viewers_after30: after30,
                    retention30_percent: 0.,
                });
            }
        }
    }
    result.correlations = groups(rows.into_iter(), |r| {
        (r.event_type.to_lowercase(), r.event_name.to_lowercase())
    })
    .into_iter()
    .map(|g| CorrelationRow {
        event_name: g[0].event_name.clone(),
        event_type: g[0].event_type.clone(),
        occurrences: g.len(),
        baseline_viewers: mean(g.iter().map(|r| r.baseline_viewers)),
        viewer_delta5_minutes: mean(g.iter().map(|r| r.viewer_delta5_minutes)),
        viewer_delta10_minutes: mean(g.iter().map(|r| r.viewer_delta10_minutes)),
    })
    .collect();
    result.correlations.sort_by(|a, b| {
        b.viewer_delta10_minutes
            .total_cmp(&a.viewer_delta10_minutes)
            .then_with(|| b.occurrences.cmp(&a.occurrences))
    });
    result.correlations.truncate(20);
    result.raids = groups(raids.into_iter(), |r| {
        if r.raid_summary.trim().is_empty() {
            "raid".into()
        } else {
            r.raid_summary.to_lowercase()
        }
    })
    .into_iter()
    .map(|g| {
        let after5 = mean(g.iter().map(|r| r.viewers_after5));
        let after30 = mean(g.iter().map(|r| r.viewers_after30));
        RaidRow {
            raid_summary: if g[0].raid_summary.trim().is_empty() {
                "Raid".into()
            } else {
                g[0].raid_summary.clone()
            },
            viewers_before: mean(g.iter().map(|r| r.viewers_before)),
            viewers_after5: after5,
            viewers_after10: mean(g.iter().map(|r| r.viewers_after10)),
            viewers_after30: after30,
            retention30_percent: if after5 <= 0. {
                0.
            } else {
                (after30 / after5 * 100.).clamp(0., 250.)
            },
        }
    })
    .collect();
    result
        .raids
        .sort_by(|a, b| b.retention30_percent.total_cmp(&a.retention30_percent));
    result.raids.truncate(12);
    if let Some(row) = result
        .correlations
        .iter()
        .find(|r| r.occurrences >= 2)
        .filter(|r| r.viewer_delta10_minutes > 1.)
    {
        result.actions.push(format!("„{}“ ist nach zehn Minuten im Mittel mit {} Zuschauern verbunden. Diesen Ablauf gezielt wiederholen.", row.event_name, signed(row.viewer_delta10_minutes)));
    }
    if let Some(row) = result
        .correlations
        .iter()
        .filter(|r| r.occurrences >= 2)
        .min_by(|a, b| {
            a.viewer_delta10_minutes
                .total_cmp(&b.viewer_delta10_minutes)
        })
        .filter(|r| r.viewer_delta10_minutes < -1.)
    {
        result.actions.push(format!("Nach „{}“ fehlen nach zehn Minuten durchschnittlich {:.1} Zuschauer. Übergang, Länge oder Inhalt prüfen.", row.event_name, -row.viewer_delta10_minutes));
    }
    if let Some(row) = result.raids.first() {
        result.actions.push(format!(
            "Die beste gemessene Raid-Bindung erreicht „{}“ mit {:.0}% nach 30 Minuten.",
            row.raid_summary, row.retention30_percent
        ));
    }
    if result.correlations.is_empty() {
        result.actions.push(
            "Noch keine Ereignisse konnten mit ausreichend Zuschauer-Samples korreliert werden."
                .into(),
        );
    }
    if result.raids.is_empty() {
        result.actions.push("Raid-Bindung wird angezeigt, sobald Raid-Events und Zuschauer-Samples gemeinsam aufgezeichnet wurden.".into());
    }
    result
}

pub fn analyze(
    events: &[JournalEvent],
    days: i32,
    now: DateTime<Utc>,
    local: impl Fn(DateTime<Utc>) -> DateTime<FixedOffset>,
) -> Analysis {
    let mut indexes: HashMap<&str, usize> = HashMap::new();
    let mut all: Vec<Vec<&JournalEvent>> = vec![];
    for event in events {
        let index = *indexes.entry(&event.session_id).or_insert_with(|| {
            all.push(vec![]);
            all.len() - 1
        });
        all[index].push(event);
    }
    for session in &mut all {
        session.sort_by_key(|e| e.timestamp_utc);
    }
    let latest = all
        .iter()
        .filter_map(|s| {
            s.iter()
                .find(|e| e.kind == "session.started")
                .map(|e| (e.timestamp_utc, s))
        })
        .max_by_key(|(at, _)| *at)
        .map(|(_, s)| summary(s, &local));
    let selected: Vec<_> = all
        .into_iter()
        .filter(|s| {
            s.iter().any(|e| {
                e.kind == "session.started"
                    && e.timestamp_utc >= now - Duration::days(days.max(1) as i64)
            }) && s.iter().any(|e| e.kind == "session.ended")
        })
        .collect();
    let mut summaries: Vec<_> = selected.iter().map(|s| summary(s, &local)).collect();
    summaries.sort_by_key(|s| s.started_at);
    Analysis {
        latest,
        dashboard: dashboard(&summaries, days, now),
        content: content(&selected, days, &local),
        correlation: correlations(&selected, days),
    }
}

pub fn analyze_local(events: &[JournalEvent], days: i32, now: DateTime<Utc>) -> Analysis {
    analyze(events, days, now, |at| {
        at.with_timezone(&Local).fixed_offset()
    })
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ActionItem {
    pub id: String,
    pub title: String,
    pub metric: String,
    pub baseline: f64,
    pub target: f64,
    pub priority: i32,
    pub status: String,
    pub created_at: DateTime<FixedOffset>,
    pub completed_at: Option<DateTime<FixedOffset>>,
    pub current_value: Option<f64>,
    #[serde(flatten)]
    pub extra: HashMap<String, Value>,
}
model!(ActionPlan { items: Vec<ActionItem>, open_count: usize, completed_count: usize });
model!(EffectivenessRow {
    id: String, title: String, metric: String, status: String, baseline: f64, current: f64,
    target: f64, improvement: f64, progress_percent: f64, verdict: String,
    created_at: DateTime<FixedOffset>, completed_at: Option<DateTime<FixedOffset>>,
});
model!(Effectiveness { rows: Vec<EffectivenessRow>, improved_count: usize, declined_count: usize, reached_count: usize, summary: String });
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Experiment {
    pub id: String,
    pub action_id: String,
    pub title: String,
    pub metric: String,
    pub baseline: f64,
    pub target_sessions: usize,
    pub status: String,
    pub started_at: DateTime<FixedOffset>,
    pub completed_at: Option<DateTime<FixedOffset>>,
    #[serde(flatten)]
    pub extra: HashMap<String, Value>,
}
model!(ExperimentRow {
    id: String, action_id: String, title: String, metric: String, status: String,
    baseline: f64, current: f64, delta: f64, session_count: usize, target_sessions: usize,
    confidence: String, verdict: String, started_at: DateTime<FixedOffset>, completed_at: Option<DateTime<FixedOffset>>,
});
model!(Experiments { rows: Vec<ExperimentRow>, active_count: usize, completed_count: usize, positive_count: usize, summary: String });
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct IntelligenceSnapshot {
    #[serde(flatten)]
    pub analysis: Analysis,
    pub actions: Option<ActionPlan>,
    pub effectiveness: Option<Effectiveness>,
    pub experiments: Option<Experiments>,
    pub recording: bool,
    pub warnings: Vec<String>,
    pub directory: String,
}
pub struct CreatorIntelligenceRuntime {
    root: PathBuf,
    history: Arc<StreamHistoryRuntime>,
    writes: Mutex<()>,
}
fn completed(status: &str) -> bool {
    matches!(status, "Erledigt" | "Automatisch erreicht")
}
fn metric(dashboard: &Dashboard, metric: &str) -> f64 {
    match metric {
        "retention" => dashboard.average_retention_percent,
        "engagement" => dashboard.average_chat_messages_per_hour,
        "score" => dashboard.average_creator_score,
        "growth" => dashboard.average_followers_per_hour,
        _ => 0.,
    }
}
fn session_metric(session: &Summary, metric: &str) -> f64 {
    match metric {
        "retention" => session.retention_percent,
        "engagement" => session.chat_messages_per_hour,
        "score" => session.creator_score as f64,
        "growth" => session.followers_per_hour,
        _ => 0.,
    }
}
fn read_items<T: serde::de::DeserializeOwned>(path: &Path) -> Result<Vec<T>, String> {
    let bytes = match fs::read_to_string(path) {
        Ok(bytes) => bytes,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(vec![]),
        Err(e) => return Err(format!("{}: {e}", path.display())),
    };
    serde_json::from_str(bytes.trim_start_matches('\u{feff}')).map_err(|e| {
        format!(
            "{}: Datei beschädigt oder Format unbekannt; Original bleibt erhalten. {e}",
            path.display()
        )
    })
}
fn write_items<T: Serialize>(path: &Path, items: &[T]) -> Result<(), String> {
    let bytes = serde_json::to_vec_pretty(items).map_err(|e| e.to_string())?;
    // Avoid unnecessary writes during every periodic refresh.
    if fs::read(path).ok().as_deref() == Some(bytes.as_slice()) {
        return Ok(());
    }
    checkpoint(path, &bytes)
}
impl CreatorIntelligenceRuntime {
    pub fn new(data_root: PathBuf, history: Arc<StreamHistoryRuntime>) -> Self {
        Self {
            root: data_root.join("CreatorIntelligence"),
            history,
            writes: Mutex::new(()),
        }
    }
    fn read_journal(&self) -> Result<(Vec<JournalEvent>, Vec<String>, bool), String> {
        let (raw, mut warnings, recording) = self.history.analysis_journal()?;
        let mut seen = HashSet::new();
        let mut events = vec![];
        let mut invalid = 0;
        for value in raw {
            let id = value["EventId"]
                .as_str()
                .filter(|s| !s.is_empty())
                .map(str::to_string);
            match serde_json::from_value::<JournalEvent>(value) {
                Ok(event)
                    if !event.session_id.trim().is_empty() && !event.kind.trim().is_empty() =>
                {
                    if id.is_none_or(|id| seen.insert(id)) {
                        events.push(event);
                    }
                }
                _ => invalid += 1,
            }
        }
        if invalid > 0 {
            warnings.push(format!(
                "{invalid} ungültige Analyse-Ereignisse übersprungen; Original bleibt erhalten."
            ));
        }
        Ok((events, warnings, recording))
    }
    pub fn snapshot(&self, days: i32) -> Result<IntelligenceSnapshot, String> {
        self.snapshot_at(days, Utc::now())
    }
    pub fn snapshot_at(
        &self,
        days: i32,
        now: DateTime<Utc>,
    ) -> Result<IntelligenceSnapshot, String> {
        if !(1..=3650).contains(&days) {
            return Err("Analysezeitraum muss zwischen 1 und 3650 Tagen liegen.".into());
        }
        let _guard = self.writes.lock().unwrap();
        let (events, mut warnings, recording) = self.read_journal()?;
        let analysis = analyze_local(&events, days, now);
        let action_analysis = if days == 30 {
            analysis.clone()
        } else {
            analyze_local(&events, 30, now)
        };
        let actions = match self.update_actions(&action_analysis, now) {
            Ok(plan) => Some(plan),
            Err(e) => {
                warnings.push(e);
                None
            }
        };
        let effectiveness = actions.as_ref().map(effectiveness);
        let experiments = match self.update_experiments(&events, now) {
            Ok(report) => Some(report),
            Err(e) => {
                warnings.push(e);
                None
            }
        };
        Ok(IntelligenceSnapshot {
            analysis,
            actions,
            effectiveness,
            experiments,
            recording,
            warnings,
            directory: self.root.to_string_lossy().into_owned(),
        })
    }
    fn load_actions(&self) -> Result<Vec<ActionItem>, String> {
        let rows: Vec<ActionItem> = read_items(&self.root.join("action-plan.json"))?;
        let mut ids = HashSet::new();
        if rows
            .iter()
            .any(|r| r.id.trim().is_empty() || !ids.insert(&r.id))
        {
            return Err("Ungültige Maßnahmenkennungen; Datei bleibt erhalten.".into());
        }
        Ok(rows)
    }
    fn load_experiments(&self) -> Result<Vec<Experiment>, String> {
        let rows: Vec<Experiment> = read_items(&self.root.join("experiments.json"))?;
        let mut ids = HashSet::new();
        if rows
            .iter()
            .any(|r| r.id.trim().is_empty() || !ids.insert(&r.id) || r.target_sessions == 0)
        {
            return Err("Ungültiges Experiment; Datei bleibt erhalten.".into());
        }
        Ok(rows)
    }
    fn update_actions(
        &self,
        analysis: &Analysis,
        now: DateTime<Utc>,
    ) -> Result<ActionPlan, String> {
        let mut stored = self.load_actions()?;
        let d = &analysis.dashboard;
        let mut suggestions = vec![];
        if d.session_count > 0 {
            if d.average_retention_percent < 90. {
                suggestions.push((
                    "Zuschauerbindung auf mindestens 90 % erhöhen".into(),
                    "retention",
                    d.average_retention_percent,
                    90.,
                    1,
                ));
            }
            if d.average_chat_messages_per_hour < 15. {
                suggestions.push((
                    "Mindestens 15 Chatnachrichten pro Stunde erreichen".into(),
                    "engagement",
                    d.average_chat_messages_per_hour,
                    15.,
                    2,
                ));
            }
            if d.creator_score_trend < 1. {
                suggestions.push((
                    "Creator Score gegenüber dem aktuellen Niveau um 5 Punkte steigern".into(),
                    "score",
                    d.average_creator_score,
                    (d.average_creator_score + 5.).min(100.),
                    2,
                ));
            }
            if d.average_followers_per_hour < 1. {
                suggestions.push((
                    "Follower-Rate auf mindestens 1 pro Stunde erhöhen".into(),
                    "growth",
                    d.average_followers_per_hour,
                    1.,
                    3,
                ));
            }
        }
        suggestions.extend(
            analysis
                .correlation
                .actions
                .iter()
                .take(3)
                .map(|s| (s.clone(), "manual", 0., 1., 3)),
        );
        let local = now.with_timezone(&Local).fixed_offset();
        for (title, metric, baseline, target, priority) in suggestions {
            if stored
                .iter()
                .any(|r| r.title.to_lowercase() == title.to_lowercase())
            {
                continue;
            }
            stored.push(ActionItem {
                id: uuid::Uuid::new_v4().simple().to_string(),
                title,
                metric: metric.into(),
                baseline,
                target,
                priority,
                status: "Offen".into(),
                created_at: local,
                completed_at: None,
                current_value: None,
                extra: HashMap::new(),
            });
        }
        for item in &mut stored {
            if item.status == "Erledigt" || item.metric == "manual" || d.session_count == 0 {
                continue;
            }
            let current = metric(d, &item.metric);
            item.current_value = Some(current);
            if current >= item.target {
                item.status = "Automatisch erreicht".into();
            }
            if item.status == "Automatisch erreicht" && item.completed_at.is_none() {
                item.completed_at = Some(local);
            }
        }
        stored.sort_by(|a, b| {
            completed(&a.status)
                .cmp(&completed(&b.status))
                .then_with(|| a.priority.cmp(&b.priority))
                .then_with(|| a.created_at.cmp(&b.created_at))
        });
        write_items(&self.root.join("action-plan.json"), &stored)?;
        Ok(ActionPlan {
            open_count: stored.iter().filter(|r| r.status == "Offen").count(),
            completed_count: stored.iter().filter(|r| completed(&r.status)).count(),
            items: stored,
        })
    }
    pub fn complete_action(&self, id: &str) -> Result<(), String> {
        self.complete_action_at(id, Utc::now())
    }
    pub fn complete_action_at(&self, id: &str, now: DateTime<Utc>) -> Result<(), String> {
        let _guard = self.writes.lock().unwrap();
        let mut items = self.load_actions()?;
        let row = items
            .iter_mut()
            .find(|r| r.id == id)
            .ok_or("Maßnahme nicht gefunden; bitte aktualisieren.")?;
        row.status = "Erledigt".into();
        row.completed_at = Some(now.with_timezone(&Local).fixed_offset());
        write_items(&self.root.join("action-plan.json"), &items)?;
        self.history.notify_change();
        Ok(())
    }
    pub fn start_experiment(&self, action_id: &str) -> Result<(), String> {
        self.start_experiment_at(action_id, Utc::now())
    }
    pub fn start_experiment_at(&self, action_id: &str, now: DateTime<Utc>) -> Result<(), String> {
        let _guard = self.writes.lock().unwrap();
        let (events, _, _) = self.read_journal()?;
        let mut items = self.load_experiments()?;
        let plan = self.update_actions(&analyze_local(&events, 30, now), now)?;
        let action = plan
            .items
            .iter()
            .find(|r| r.id == action_id)
            .ok_or("Maßnahme nicht gefunden; bitte aktualisieren.")?;
        if action.metric == "manual" {
            return Err("Für manuelle Maßnahmen ist keine messbare Kennzahl verfügbar.".into());
        }
        if items
            .iter()
            .any(|r| r.action_id == action_id && r.status == "Aktiv")
        {
            return Ok(());
        }
        items.push(Experiment {
            id: uuid::Uuid::new_v4().simple().to_string(),
            action_id: action_id.into(),
            title: action.title.clone(),
            metric: action.metric.clone(),
            baseline: action.baseline,
            target_sessions: 3,
            status: "Aktiv".into(),
            started_at: now.with_timezone(&Local).fixed_offset(),
            completed_at: None,
            extra: HashMap::new(),
        });
        write_items(&self.root.join("experiments.json"), &items)?;
        self.history.notify_change();
        Ok(())
    }
    fn update_experiments(
        &self,
        events: &[JournalEvent],
        now: DateTime<Utc>,
    ) -> Result<Experiments, String> {
        let mut items = self.load_experiments()?;
        let mut sessions = vec![];
        for mut rows in groups(events.iter(), |e| e.session_id.clone()) {
            rows.sort_by_key(|e| e.timestamp_utc);
            if rows.iter().any(|e| e.kind == "session.started")
                && rows.iter().any(|e| e.kind == "session.ended")
            {
                sessions.push(summary(&rows, &|at| {
                    at.with_timezone(&Local).fixed_offset()
                }));
            }
        }
        sessions.sort_by_key(|s| s.started_at);
        let mut rows = vec![];
        for item in &mut items {
            let before: Vec<_> = sessions
                .iter()
                .filter(|s| s.started_at < item.started_at)
                .rev()
                .take(3)
                .collect();
            let during: Vec<_> = sessions
                .iter()
                .filter(|s| {
                    s.started_at >= item.started_at
                        && item.completed_at.is_none_or(|end| s.started_at <= end)
                })
                .take(item.target_sessions)
                .collect();
            let baseline = if before.is_empty() {
                item.baseline
            } else {
                mean(before.iter().map(|s| session_metric(s, &item.metric)))
            };
            let current = if during.is_empty() {
                baseline
            } else {
                mean(during.iter().map(|s| session_metric(s, &item.metric)))
            };
            let delta = current - baseline;
            if item.status == "Aktiv" && during.len() >= item.target_sessions {
                item.status = "Ausgewertet".into();
                item.completed_at = Some(now.with_timezone(&Local).fixed_offset());
            }
            let threshold = (baseline.abs() * 0.05).max(0.5);
            rows.push(ExperimentRow {
                id: item.id.clone(),
                action_id: item.action_id.clone(),
                title: item.title.clone(),
                metric: item.metric.clone(),
                status: item.status.clone(),
                baseline,
                current,
                delta,
                session_count: during.len(),
                target_sessions: item.target_sessions,
                confidence: if during.len() >= 3 && before.len() >= 3 {
                    "Mittel"
                } else if during.len() >= 2 {
                    "Vorläufig"
                } else {
                    "Zu wenig Daten"
                }
                .into(),
                verdict: if during.is_empty() {
                    "Noch kein vollständiger Stream seit Teststart."
                } else if delta > threshold {
                    "Positive Veränderung beobachtet."
                } else if delta < -threshold {
                    "Negative Veränderung beobachtet."
                } else {
                    "Noch kein klarer Unterschied erkennbar."
                }
                .into(),
                started_at: item.started_at,
                completed_at: item.completed_at,
            });
        }
        write_items(&self.root.join("experiments.json"), &items)?;
        rows.sort_by(|a, b| {
            (b.status == "Aktiv")
                .cmp(&(a.status == "Aktiv"))
                .then_with(|| b.started_at.cmp(&a.started_at))
        });
        let active = rows.iter().filter(|r| r.status == "Aktiv").count();
        let completed = rows.iter().filter(|r| r.status == "Ausgewertet").count();
        let positive = rows
            .iter()
            .filter(|r| r.delta > (r.baseline.abs() * 0.05).max(0.5))
            .count();
        let message = if rows.is_empty() {
            "Noch keine Experimente gestartet. Wähle eine messbare Maßnahme aus und starte daraus einen Test.".into()
        } else {
            format!("{active} aktiv · {completed} ausgewertet · {positive} mit positiver beobachteter Veränderung.")
        };
        Ok(Experiments {
            rows,
            active_count: active,
            completed_count: completed,
            positive_count: positive,
            summary: message,
        })
    }
    pub fn note(&self, note: &str, request_id: &str) -> Result<(), String> {
        self.note_at(note, request_id, Utc::now())
    }
    pub fn note_at(&self, note: &str, request_id: &str, now: DateTime<Utc>) -> Result<(), String> {
        self.history.record_note(note, request_id, now)
    }
    pub fn weekly_report(&self) -> Result<String, String> {
        self.weekly_report_at(Utc::now())
    }
    pub fn weekly_report_at(&self, now: DateTime<Utc>) -> Result<String, String> {
        let (events, warnings, _) = self.read_journal()?;
        let analysis = analyze_local(&events, 7, now);
        let d = &analysis.dashboard;
        let mut html = String::from("<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>Creator Intelligence Wochenbericht</title><style>body{font-family:Segoe UI,Arial;background:#0b1014;color:#eef3f6;margin:32px}section{background:#11191f;border:1px solid #29343c;border-radius:12px;padding:18px;margin:14px 0}li{margin:7px 0}</style></head><body><h1>Creator Intelligence Wochenbericht</h1>");
        html.push_str(&format!("<p>Erstellt am {}</p><section><h2>Kennzahlen</h2><p>Streams: {} · Creator Score: {:.1} · Ø Zuschauer: {:.1} · Bindung: {:.0}%</p></section>", now.with_timezone(&Local).format("%d.%m.%Y %H:%M"), d.session_count, d.average_creator_score, d.average_viewers, d.average_retention_percent));
        if !warnings.is_empty() {
            html.push_str("<section><h2>Datenhinweise</h2><ul>");
            for warning in warnings {
                html.push_str(&format!("<li>{}</li>", escape(&warning)));
            }
            html.push_str("</ul></section>");
        }
        html.push_str("<section><h2>Empfehlungen</h2><ul>");
        let mut seen = HashSet::new();
        for insight in d
            .insights
            .iter()
            .chain(&analysis.content.insights)
            .chain(&analysis.correlation.actions)
        {
            if seen.insert(insight) {
                html.push_str(&format!("<li>{}</li>", escape(insight)));
            }
        }
        html.push_str("</ul></section><section><h2>Stärkste Szenen</h2><ul>");
        for row in analysis.content.scenes.iter().take(8) {
            html.push_str(&format!(
                "<li>{} · {} Zuschauer · Ø {:.1}</li>",
                escape(&row.name),
                signed(row.viewer_delta),
                row.average_viewers
            ));
        }
        html.push_str("</ul></section><section><h2>Ereigniswirkung</h2><ul>");
        for row in analysis.correlation.correlations.iter().take(10) {
            html.push_str(&format!(
                "<li>{} · nach 5 Min {} · nach 10 Min {}</li>",
                escape(&row.event_name),
                signed(row.viewer_delta5_minutes),
                signed(row.viewer_delta10_minutes)
            ));
        }
        html.push_str("</ul></section></body></html>");
        let path = self.root.join("Reports").join(format!(
            "creator-weekly-{}-{}.html",
            now.with_timezone(&Local).format("%Y-%m-%d-%H%M"),
            uuid::Uuid::new_v4().simple()
        ));
        checkpoint(&path, html.as_bytes())?;
        Ok(path.to_string_lossy().into_owned())
    }
}
fn escape(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&#39;")
}
fn effectiveness(plan: &ActionPlan) -> Effectiveness {
    let mut rows: Vec<_> = plan
        .items
        .iter()
        .filter(|r| r.metric != "manual")
        .map(|item| {
            let current = item.current_value.unwrap_or(item.baseline);
            let required = (item.target - item.baseline).max(0.01);
            let improvement = current - item.baseline;
            EffectivenessRow {
                id: item.id.clone(),
                title: item.title.clone(),
                metric: item.metric.clone(),
                status: item.status.clone(),
                baseline: item.baseline,
                current,
                target: item.target,
                improvement,
                progress_percent: (improvement / required * 100.).clamp(0., 200.),
                verdict: if completed(&item.status) {
                    if improvement > 0. {
                        "Ziel erreicht · positive Entwicklung"
                    } else {
                        "Ziel abgeschlossen · Wirkung noch nicht messbar"
                    }
                } else if improvement > required * 0.5 {
                    "Deutliche Verbesserung"
                } else if improvement > 0. {
                    "Leichte Verbesserung"
                } else if improvement < 0. {
                    "Wert hat sich verschlechtert"
                } else {
                    "Noch keine messbare Veränderung"
                }
                .into(),
                created_at: item.created_at,
                completed_at: item.completed_at,
            }
        })
        .collect();
    rows.sort_by(|a, b| {
        completed(&b.status)
            .cmp(&completed(&a.status))
            .then_with(|| b.progress_percent.total_cmp(&a.progress_percent))
            .then_with(|| b.improvement.total_cmp(&a.improvement))
    });
    let message = rows
        .iter()
        .filter(|r| r.improvement > 0.01)
        .max_by(|a, b| a.progress_percent.total_cmp(&b.progress_percent))
        .map_or(
            "Noch keine Maßnahme zeigt eine belastbare positive Veränderung.".into(),
            |r| {
                format!(
                    "Stärkste beobachtete Entwicklung: {} ({}; {:.0}% des Zielwegs).",
                    r.title,
                    signed(r.improvement),
                    r.progress_percent
                )
            },
        );
    Effectiveness {
        improved_count: rows.iter().filter(|r| r.improvement > 0.01).count(),
        declined_count: rows.iter().filter(|r| r.improvement < -0.01).count(),
        reached_count: rows.iter().filter(|r| completed(&r.status)).count(),
        rows,
        summary: message,
    }
}
