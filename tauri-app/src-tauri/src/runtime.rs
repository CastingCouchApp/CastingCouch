use super::*;
pub(super) fn bind_stream_history(
    obs: &ObsClient,
    history: Arc<ccs_modules::stream_history::StreamHistoryRuntime>,
    stream_end: Arc<ccs_modules::stream_end::StreamEndRuntime>,
) {
    obs.set_stream_observer(Arc::new(move |event| {
        if let Err(error) = history.observe_stream_event(event.active, event.at) {
            warn!(%error,"OBS-Sitzungsereignis konnte nicht gespeichert werden");
        }
        if !event.active {
            let runtime = stream_end.clone();
            tauri::async_runtime::spawn(async move {
                runtime.observe_obs_stopped().await;
            });
        }
    }));
}
pub(super) fn spawn_runtime(app: AppHandle) {
    stream_end_host::spawn_events(app.clone(), app.state::<AppState>().stream_end.clone());
    spawn_stream_history_events(app.clone(), app.state::<AppState>().stream_history.clone());
    spawn_extension_pack_events(app.clone(), app.state::<AppState>().hub.clone());
    spawn_obs_data(app.clone());
    spawn_twitch_data(app.clone());
    spawn_chat_catalogs(app.clone());
    spawn_watchdog(app.clone());
    spawn_music_state_monitor(app.clone());
    spawn_music_player_monitor(app.clone());
    spawn_music_overlay_monitor(app.clone());
    tauri::async_runtime::spawn(async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(1));
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        let mut previous_music = String::new();
        loop {
            tick.tick().await;
            let state = app.state::<AppState>();
            let settings = match state.settings.load().await {
                Ok(s) => s,
                Err(error) => {
                    warn!(%error,"Overlay-Einstellungen nicht lesbar");
                    continue;
                }
            };
            state.spotify.set_reconnect_enabled(
                settings.music_player.provider_id() == "spotify"
                    && settings.general.connection_watchdog_enabled
                    && settings.general.reconnect_spotify,
            );
            let metrics =
                serde_json::to_value(state.twitch_metrics.snapshot().await).unwrap_or(Value::Null);
            if let Err(error) = state
                .stream_history
                .update_context(&metrics, &settings.twitch.extra)
            {
                warn!(%error,"Sitzungskontext konnte nicht aktualisiert werden");
            }
            if let Err(error) = state
                .hub
                .configure_history(ccs_overlay_server::chat_history_path(
                    &state.paths,
                    &settings,
                ))
            {
                warn!(%error, "Chat-Verlauf konnte nicht geladen werden");
            }

            let music_snapshot = match music_player_snapshot(app.state::<AppState>()).await {
                Ok(snapshot) => snapshot,
                Err(error) => {
                    warn!(%error,"Musikdaten nicht lesbar");
                    continue;
                }
            };
            let _ = app.emit("music-player-changed", &music_snapshot);
            let music = update_music_data(&state, &music_snapshot, &settings);
            let signature = format!(
                "{}|{}|{}|{}",
                music["provider"], music["title"], music["artist"], music["coverUrl"]
            );
            if music["overlayEnabled"] == true && signature != previous_music {
                state.bridge.app_music_track(
                    music["provider"].as_str().unwrap_or("spotify"),
                    music["title"].as_str().unwrap_or(""),
                    music["artist"].as_str().unwrap_or(""),
                    music["coverUrl"].as_str().unwrap_or(""),
                );
                previous_music = signature;
            }
            let runtime = state.alerts.runtime().await.ok();
            let scene = state.obs.current_program_scene().await.unwrap_or_default();
            let snapshot = state.hub.live.merge_snapshot(&json!({
                "obs":{"currentScene":scene,"connected":state.obs.status().await.state==ccs_modules::ConnectionState::Connected},
                "stream":{"currentScene":scene},
                "branding":{"displayName":settings.branding.display_name,"channelName":settings.branding.channel_name,"accentColor":settings.branding.accent_color,"logoPath":settings.branding.logo_path},
                "alerts":runtime.map(|r|json!({"isRunning":r.current_type.is_some(),"currentType":r.current_type.unwrap_or_default(),"queueLength":r.pending_count})).unwrap_or_else(||json!({"isRunning":false,"currentType":"","queueLength":0})),
                "countdown":state.hub.live.countdown_state()
            }));

            let path = ccs_core::paths::overlay_data_path(&state.paths, &settings);
            match state
                .hub
                .live
                .write_snapshot_with_music(&path, music["overlayEnabled"] == true)
                .await
            {
                Ok(()) => state.hub.live.data.write().unwrap()["dataError"] = Value::Null,
                Err(error) => {
                    warn!(%error,"Overlay-Daten konnten nicht geschrieben werden");
                    state.hub.live.data.write().unwrap()["dataError"] = json!(format!(
                        "Overlay-Daten konnten nicht geschrieben werden: {error}"
                    ));
                }
            }
            let current_data = state.hub.live.data.read().unwrap().clone();
            state.hub.publish(&json!({"source":"app","type":"app.overlay.data","at":snapshot["updatedAt"],"data":current_data}));
            if let Err(error) = state.hub.flush_history() {
                warn!(%error,"Chat-Verlauf konnte nicht gespeichert werden");
            }
            state.hub.publish(&state.hub.countdown());
        }
    });
}
pub(super) fn spawn_stream_history_events<R: tauri::Runtime>(
    app: AppHandle<R>,
    history: Arc<ccs_modules::stream_history::StreamHistoryRuntime>,
) {
    let mut changes = history.subscribe_changes();
    tauri::async_runtime::spawn(async move {
        loop {
            match changes.recv().await {
                Ok(()) | Err(broadcast::error::RecvError::Lagged(_)) => {
                    let _ = app.emit("stream-history-changed", json!({"changed":true}));
                }
                Err(broadcast::error::RecvError::Closed) => break,
            }
        }
    });
}

fn spawn_chat_catalogs(app: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(3));
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        loop {
            tick.tick().await;
            let Some(state) = app.try_state::<AppState>() else {
                break;
            };
            if state.scene_music.is_closed() {
                break;
            }
            let Ok(settings) = state.settings.load().await else {
                continue;
            };
            if settings.twitch.enable_chat
                && state.twitch.status().await.state == ccs_modules::ConnectionState::Connected
            {
                state
                    .twitch
                    .refresh_chat_catalogs(
                        &settings.twitch.client_id,
                        &settings.twitch.channel_name,
                        &settings.overlay.chat,
                        false,
                    )
                    .await;
            }
        }
    });
}

fn spawn_music_state_monitor(app: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(5));
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        let mut startup = true;
        loop {
            tick.tick().await;
            let state = app.state::<AppState>();
            if state.scene_music.is_closed() {
                break;
            }
            match state.music_states.tick(startup).await {
                Ok(()) => startup = false,
                Err(error) => warn!(%error, "Spotify-ZustandsÃ¼berwachung fehlgeschlagen"),
            }
        }
    });
}

fn spawn_obs_data(app: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(3));
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        let mut counter = 0u64;
        loop {
            tick.tick().await;
            let state = app.state::<AppState>();
            let Ok(settings) = state.settings.load().await else {
                continue;
            };

            let revision = state.obs.stream_revision();
            let requested_at = state.stream_history.begin_poll();
            let outputs = state.obs.output_status().await.unwrap_or(Value::Null);
            if outputs
                .pointer("/stream/outputActive")
                .and_then(Value::as_bool)
                == Some(false)
            {
                state.stream_end.observe_obs_stopped().await;
            }
            let scene = state.obs.current_program_scene().await.unwrap_or_default();
            let metrics =
                serde_json::to_value(state.twitch_metrics.snapshot().await).unwrap_or(Value::Null);
            // A stream event received during the request is newer than the queried
            // snapshot. Keep its state instead of overwriting it with a delayed reply.
            if revision == state.obs.stream_revision() {
                let stream = match outputs
                    .pointer("/stream/outputActive")
                    .and_then(Value::as_bool)
                {
                    Some(active) => {
                        json!({"available":true,"isLive":active,"elapsedSeconds":outputs.pointer("/stream/outputDuration").and_then(Value::as_u64).unwrap_or(0)/1000})
                    }
                    None => json!({"available":false}),
                };
                let snapshot =
                    json!({"obs":{"outputs":outputs,"currentScene":scene},"stream":stream});
                if let Err(error) = state.stream_history.observe_polled_now(
                    &snapshot,
                    &metrics,
                    &settings.twitch.extra,
                    requested_at,
                ) {
                    warn!(%error,"Sitzungsverlauf fehlgeschlagen");
                }
            }
            state
                .scene_music
                .observe_stream(
                    outputs
                        .pointer("/stream/outputActive")
                        .and_then(Value::as_bool),
                )
                .await;
            use ccs_modules::obs::ObsQuery;
            let (mic, desktop) = tokio::join!(
                state.obs.query(ObsQuery::Mute {
                    input_name: settings.obs.microphone_source.clone()
                }),
                state.obs.query(ObsQuery::Mute {
                    input_name: settings.obs.desktop_audio_source.clone()
                })
            );
            state.hub.live.merge_snapshot(&json!({"obs":{"microphoneMuted":mic.as_ref().ok().and_then(|v|v["inputMuted"].as_bool()).unwrap_or(false),"microphoneAvailable":mic.is_ok(),"desktopAudioMuted":desktop.as_ref().ok().and_then(|v|v["inputMuted"].as_bool()).unwrap_or(false),"desktopAudioAvailable":desktop.is_ok()}}));

            counter = counter.wrapping_add(1);
            let _ = counter;
        }
    });
}

fn spawn_twitch_data(app: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(1));
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        loop {
            tick.tick().await;
            let state = app.state::<AppState>();
            match state.twitch_metrics.refresh(false).await {
                Ok(true) => {
                    let _ = app.emit(
                        "twitch-metrics-changed",
                        state.twitch_metrics.snapshot().await,
                    );
                }
                Ok(false) => {}
                Err(error) => {
                    warn!(%error,"Twitch-Zahlen konnten nicht aktualisiert werden");
                }
            }
        }
    });
}

fn spawn_watchdog(app: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(1));
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        let mut counter = 0u64;
        loop {
            tick.tick().await;
            let state = app.state::<AppState>();
            let Ok(settings) = state.settings.load().await else {
                continue;
            };
            if counter % 15 == 0 {
                if let Some(music) = state.alerts.music_ducking() {
                    if let Ok(Err(error)) = tokio::time::timeout(
                        std::time::Duration::from_secs(8),
                        music.retry_restore(),
                    )
                    .await
                    {
                        warn!(%error, "MusiklautstÃ¤rke konnte noch nicht wiederhergestellt werden");
                    }
                }
            }
            if counter % 15 == 0
                && state
                    .overlay
                    .lock()
                    .await
                    .as_ref()
                    .is_none_or(|s| !s.is_running())
            {
                let _guard = state.settings_mutation.lock().await;
                let Ok(settings) = state.settings.load().await else {
                    continue;
                };
                match OverlayServer::start(
                    state.settings.clone(),
                    state.paths.clone(),
                    state.hub.clone(),
                    settings.overlay.web_server_port,
                )
                .await
                {
                    Ok(server) => {
                        *state.overlay.lock().await = Some(server);
                        state.hub.live.data.write().unwrap()["serverError"] = Value::Null;
                    }
                    Err(error) => {
                        state.hub.live.data.write().unwrap()["serverError"] =
                            json!(error.to_string());
                    }
                }
            }
            if counter % settings.general.connection_watchdog_seconds.max(1) as u64 == 0
                && settings.general.connection_watchdog_enabled
                && settings.general.reconnect_twitch
                && state
                    .twitch
                    .needs_reconnect(settings.twitch.enable_event_sub)
                    .await
            {
                if let Err(error) = state
                    .twitch
                    .connect(&TwitchConnectOptions {
                        client_id: settings.twitch.client_id.clone(),
                        channel_name: settings.twitch.channel_name.clone(),
                        scopes: settings.twitch.scopes.clone(),
                        enable_event_sub: settings.twitch.enable_event_sub,
                    })
                    .await
                {
                    warn!(%error,"Twitch-Neuverbindung fehlgeschlagen");
                }
            }
            counter = counter.wrapping_add(1);
            let _ = counter;
        }
    });
}

fn spawn_music_player_monitor(app: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(5));
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        loop {
            tick.tick().await;
            let state = app.state::<AppState>();
            if state.scene_music.is_closed() {
                break;
            }
            state.music_player.refresh_details().await;
        }
    });
}

pub(super) fn update_music_data(
    state: &AppState,
    snapshot: &ccs_modules::music_player::MusicPlayerSnapshot,
    settings: &AppSettings,
) -> Value {
    state.stream_history.record_music_now(snapshot);
    let data = state.music_overlay.snapshot(snapshot, settings);
    if data["overlayEnabled"] == true {
        state
            .hub
            .live
            .merge_snapshot(&json!({"music":data,"spotify":data}));
    }
    data
}
fn spawn_music_overlay_monitor(app: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(1));
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        loop {
            tick.tick().await;
            let state = app.state::<AppState>();
            if state.scene_music.is_closed() {
                break;
            }
            if let Err(error) = state.music_overlay.tick().await {
                warn!(%error,"Musik-Overlay in OBS konnte nicht synchronisiert werden");
            }
        }
    });
}
