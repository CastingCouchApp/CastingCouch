//! Read-only C# dashboard checklist. No stream or provider mutation.
use crate::{ConnectionState, ServiceStatus};
use serde::Serialize;
use serde_json::Value;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PreflightCheck {
    pub key: String,
    pub label: String,
    pub ok: bool,
    pub detail: String,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PreflightSnapshot {
    pub checked_at: String,
    pub warning_count: u32,
    pub checks: Vec<PreflightCheck>,
}
pub fn evaluate(
    settings: &Value,
    obs: &ServiceStatus,
    twitch: &ServiceStatus,
    music: &ServiceStatus,
    channel: Result<&Value, &str>,
) -> PreflightSnapshot {
    let mut checks = Vec::new();
    let mut add = |key: &str, label: &str, ok: bool, detail: &str| {
        checks.push(PreflightCheck {
            key: key.into(),
            label: label.into(),
            ok,
            detail: detail.into(),
        })
    };
    for (key, label, service) in [
        ("obs", "OBS WebSocket verbunden", obs),
        ("twitch", "Twitch verbunden", twitch),
        ("music", music.name.as_str(), music),
    ] {
        add(
            key,
            label,
            service.state == ConnectionState::Connected,
            &service.detail,
        );
    }
    for (key, label, field) in [
        ("start_scene", "Startszene konfiguriert", "StartScene"),
        ("live_scene", "Liveszene konfiguriert", "LiveScene"),
    ] {
        let value = settings["Obs"][field].as_str().unwrap_or("");
        add(key, label, !value.trim().is_empty(), value);
    }
    for (key, label, field) in [
        ("title", "Streamtitel gesetzt", "title"),
        ("category", "Twitch-Kategorie gesetzt", "game_name"),
    ] {
        let detail = channel
            .map(|v| v["data"][0][field].as_str().unwrap_or(""))
            .unwrap_or_else(|e| e);
        add(
            key,
            label,
            channel.is_ok() && !detail.trim().is_empty(),
            detail,
        );
    }
    let starts = settings["Spotify"]
        .get("StartOnStreamStart")
        .and_then(Value::as_bool)
        .or_else(|| {
            settings["Workflow"]
                .get("AutoStartSpotifyPlaylist")
                .and_then(Value::as_bool)
        })
        .unwrap_or(true);
    let required = music.id == "spotify" && starts;
    let playlist = settings["Spotify"]["StartPlaylistUri"]
        .as_str()
        .unwrap_or("");
    add(
        "playlist",
        "Spotify-Startplaylist konfiguriert",
        !required || !playlist.trim().is_empty(),
        if required {
            playlist
        } else {
            "Für diese Auswahl nicht erforderlich"
        },
    );
    let raids = settings["Twitch"]["RaidOnStreamEnd"]
        .as_bool()
        .unwrap_or(false);
    let target = settings["Twitch"]["SelectedRaidChannel"]
        .as_str()
        .unwrap_or("");
    add(
        "raid",
        "Raid-Ziel für geplantes Streamende gesetzt",
        !raids || !target.trim().is_empty(),
        if raids {
            target
        } else {
            "Für diese Auswahl nicht erforderlich"
        },
    );
    PreflightSnapshot {
        checked_at: chrono::Utc::now().to_rfc3339(),
        warning_count: checks.iter().filter(|c| !c.ok).count() as u32,
        checks,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    fn connected(id: &str) -> ServiceStatus {
        ServiceStatus {
            id: id.into(),
            name: id.into(),
            state: ConnectionState::Connected,
            detail: String::new(),
        }
    }
    #[test]
    fn preserves_legacy_flags_and_checks_fresh_values_without_excluded_modules() {
        let channel = json!({"data":[{"title":"Live","game_name":"Game"}]});
        let mut settings = json!({"Obs":{"StartScene":"Intro","LiveScene":"Game"},"Spotify":{"StartPlaylistUri":""},"Workflow":{"AutoStartSpotifyPlaylist":false},"Twitch":{"RaidOnStreamEnd":true,"SelectedRaidChannel":"target"}});
        let check = |settings: &Value, music: &str, channel: Result<&Value, &str>| {
            evaluate(
                settings,
                &connected("obs"),
                &connected("twitch"),
                &connected(music),
                channel,
            )
        };
        let first = check(&settings, "spotify", Ok(&channel));
        assert_eq!(first.checks.len(), 9);
        assert_eq!(first.warning_count, 0);
        settings["Spotify"]["StartOnStreamStart"] = json!(true);
        assert_eq!(check(&settings, "spotify", Ok(&channel)).warning_count, 1);
        assert_eq!(check(&settings, "ytmusic", Ok(&channel)).warning_count, 0);
        let denied = check(&settings, "ytmusic", Err("Forbidden"));
        assert_eq!(denied.warning_count, 2);
        assert!(denied
            .checks
            .iter()
            .filter(|c| matches!(c.key.as_str(), "title" | "category"))
            .all(|c| !c.ok && c.detail == "Forbidden"));
    }
}
