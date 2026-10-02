//! Music operations shared by native commands and alert execution.
use crate::{
    spotify::{SpotifyClient, SpotifyQuery},
    ModuleError, ModuleResult,
};
use serde_json::Value;
use std::{collections::HashSet, sync::Arc, time::Duration};
use tokio::sync::Mutex;

struct RestoreVolume {
    client_id: String,
    device_id: String,
    original: u8,
    current: u8,
    needs_write: bool,
    fade_in_ms: u64,
}
#[derive(Default)]
struct DuckingState {
    active: HashSet<String>,
    queued: bool,
    restore: Option<RestoreVolume>,
}

pub struct AlertDucking {
    player: Arc<SpotifyClient>,
    state: Mutex<DuckingState>,
}
impl AlertDucking {
    pub fn new(player: Arc<SpotifyClient>) -> Self {
        Self {
            player,
            state: Mutex::new(DuckingState::default()),
        }
    }

    pub async fn begin(&self, id: &str, client_id: &str, options: &Value) -> ModuleResult<()> {
        let mut state = self.state.lock().await;
        if !state.active.insert(id.into()) || state.restore.is_some() {
            return Ok(());
        }
        if !flag(options, "SmartAutomationEnabled", true)
            || !flag(options, "MuteDuringAlerts", true)
            || options["AlertDuckingMode"]
                .as_str()
                .is_some_and(|mode| mode.eq_ignore_ascii_case("None"))
        {
            return Ok(());
        }
        let playback = self
            .player
            .query(client_id, SpotifyQuery::Playback, None)
            .await?;
        if playback["is_playing"] != true {
            return Ok(());
        }
        let Some(device_id) = playback["device"]["id"]
            .as_str()
            .filter(|id| !id.is_empty())
        else {
            return Ok(());
        };
        let Some(original) = playback["device"]["volume_percent"]
            .as_u64()
            .filter(|v| *v <= 100)
        else {
            return Ok(());
        };
        let target = options["AlertMuteVolumePercent"]
            .as_u64()
            .unwrap_or(75)
            .min(original) as u8;
        if target == original as u8 {
            return Ok(());
        }
        let fade = flag(options, "FadeDuringAlerts", true);
        state.restore = Some(RestoreVolume {
            client_id: client_id.into(),
            device_id: device_id.into(),
            original: original as u8,
            current: original as u8,
            needs_write: false,
            fade_in_ms: if fade {
                number(options, "AlertFadeInMilliseconds", 500)
            } else {
                0
            },
        });
        self.fade(
            state.restore.as_mut().unwrap(),
            target,
            if fade {
                number(options, "AlertFadeOutMilliseconds", 500)
            } else {
                0
            },
        )
        .await
    }

    pub async fn end(&self, id: &str, queued: bool) -> ModuleResult<()> {
        let mut state = self.state.lock().await;
        state.active.remove(id);
        state.queued = queued;
        self.restore_if_idle(&mut state).await
    }

    /// Retry a failed restore without disturbing a currently playing/queued alert.
    pub async fn retry_restore(&self) -> ModuleResult<()> {
        self.restore_if_idle(&mut *self.state.lock().await).await
    }

    async fn restore_if_idle(&self, state: &mut DuckingState) -> ModuleResult<()> {
        if !state.active.is_empty() || state.queued {
            return Ok(());
        }
        if let Some(restore) = &mut state.restore {
            self.fade(restore, restore.original, restore.fade_in_ms)
                .await?;
            state.restore = None; // retain original volume until restoration succeeds
        }
        Ok(())
    }

    async fn fade(
        &self,
        restore: &mut RestoreVolume,
        target: u8,
        milliseconds: u64,
    ) -> ModuleResult<()> {
        let milliseconds = milliseconds.min(5000);
        let steps = if milliseconds == 0 {
            1
        } else {
            (milliseconds / 100).clamp(2, 10)
        };
        let start = restore.current as f64;
        for step in 1..=steps {
            let percent = (start + (target as f64 - start) * step as f64 / steps as f64)
                .round()
                .clamp(0.0, 100.0) as u8;
            if percent != restore.current || restore.needs_write {
                // A cancelled HTTP future may already have changed the physical device.
                // Keep the requested value and uncertainty until a response confirms it.
                restore.current = percent;
                restore.needs_write = true;
                self.player
                    .set_device_volume(&restore.client_id, &restore.device_id, percent)
                    .await?;
                restore.needs_write = false;
            }
            if step < steps {
                tokio::time::sleep(Duration::from_millis((milliseconds / steps).max(50))).await;
            }
        }
        Ok(())
    }

    /// Manual volume changes during alerts become the desired restoration volume.
    pub async fn set_volume(&self, client_id: &str, percent: u8) -> ModuleResult<Value> {
        if percent > 100 {
            return Err(ModuleError::Message(
                "Lautstärke muss zwischen 0 und 100 liegen".into(),
            ));
        }
        let mut state = self.state.lock().await;
        if let Some(restore) = &mut state.restore {
            restore.original = percent;
            Ok(serde_json::json!({"deferred":true}))
        } else {
            self.player
                .action(client_id, crate::spotify::SpotifyAction::Volume { percent })
                .await
        }
    }
}
fn flag(options: &Value, name: &str, default: bool) -> bool {
    options[name].as_bool().unwrap_or(default)
}
fn number(options: &Value, name: &str, default: u64) -> u64 {
    options[name].as_u64().unwrap_or(default).min(5000)
}
