use super::*;
use chrono::{Duration as ChronoDuration, Utc};
use serde_json::json;
use std::collections::{BTreeMap, VecDeque};
use std::sync::atomic::{AtomicBool, Ordering};
use tokio::sync::Notify;

#[derive(Default)]
struct FakeIo {
    calls: Mutex<Vec<StreamEndOperation>>,
    replies: Mutex<VecDeque<(StreamEndOperation, Result<StreamEndReply, String>)>>,
    hold_start: AtomicBool,
    release_start: Notify,
    hold_cancel: AtomicBool,
    release_cancel: Notify,
    hold_probe: AtomicBool,
    release_probe: Notify,
}
impl StreamEndIo for FakeIo {
    fn execute(&self, operation: StreamEndOperation) -> IoFuture<'_> {
        Box::pin(async move {
            self.calls.lock().await.push(operation.clone());
            if matches!(operation, StreamEndOperation::StartRaid { .. })
                && self.hold_start.load(Ordering::SeqCst)
            {
                self.release_start.notified().await;
            }
            if operation == StreamEndOperation::CancelRaid
                && self.hold_cancel.load(Ordering::SeqCst)
            {
                self.release_cancel.notified().await;
            }
            if matches!(operation, StreamEndOperation::ProbeRaid { .. })
                && self.hold_probe.load(Ordering::SeqCst)
            {
                self.release_probe.notified().await;
            }
            let mut replies = self.replies.lock().await;
            if replies.front().is_some_and(|(op, _)| *op == operation) {
                return replies.pop_front().unwrap().1;
            }
            Ok(match operation {
                StreamEndOperation::ProbeRaid { .. } => {
                    StreamEndReply::Target(Some(RaidIdentity {
                        id: "target-id".into(),
                        login: "target".into(),
                        display_name: "Target".into(),
                        online: true,
                    }))
                }
                _ => StreamEndReply::Done,
            })
        })
    }
}
fn plan(mode: &str) -> StreamEndPlan {
    StreamEndPlan {
        preferences: StreamEndPreferences {
            mode: mode.into(),
            end_scene_seconds: 3,
            selected_raid_channel: "target".into(),
            raid_countdown_seconds: 12,
            ..StreamEndPreferences::default()
        },
        end_scene: "End".into(),
        start_scene: "Start".into(),
        broadcaster_id: "own-id".into(),
        broadcaster_login: "own".into(),
        play_end_music: false,
        pause_music_on_stream_end: false,
    }
}
async fn settle() {
    for _ in 0..60 {
        tokio::task::yield_now().await;
    }
}
async fn advance(seconds: u64) {
    tokio::time::advance(Duration::from_secs(seconds)).await;
    settle().await;
}
async fn setup(mode: &str, delay: u32) -> (Arc<StreamEndRuntime>, Arc<FakeIo>) {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    runtime.start(plan(mode), delay, io.clone()).await.unwrap();
    settle().await;
    (runtime, io)
}
fn outgoing(from: &str, target: &str) -> TwitchEvent {
    TwitchEvent {
        event_type: "channel.raid".into(),
        summary: "raid".into(),
        received_at: Utc::now(),
        data: BTreeMap::from([
            ("from_broadcaster_user_id".into(), from.into()),
            ("to_broadcaster_user_id".into(), target.into()),
            ("from_broadcaster_user_login".into(), "own".into()),
            ("to_broadcaster_user_login".into(), "target".into()),
        ]),
    }
}

#[test]
fn settings_keep_csharp_enum_representation_and_unknown_fields_on_noop() {
    for mode in [json!(2), json!("EndSceneRaidThenStop")] {
        let original = json!({"Twitch":{"StreamEndMode":mode,"EndSceneDurationSeconds":0,
            "RaidStartTimeoutSeconds":0,"RaidCountdownSeconds":900,"future":{"x":1}},
            "Workflow":{"EndSceneSeconds":17,"alpha":true},"Obs":{"EndScene":"End"}});
        let prefs = StreamEndPreferences::read(&original).unwrap();
        assert_eq!(prefs.mode, "EndSceneRaidThenStop");
        assert_eq!(prefs.end_scene_seconds, 17);
        assert_eq!(prefs.raid_start_timeout_seconds, 120);
        assert_eq!(prefs.raid_countdown_seconds, 300);
        assert_eq!(prefs.apply(&original).unwrap(), original);
        let changed = StreamEndPreferences {
            end_scene_seconds: 25,
            ..prefs
        };
        let edited = changed.apply(&original).unwrap();
        assert_eq!(edited["Twitch"]["EndSceneDurationSeconds"], 25);
        assert_eq!(edited["Workflow"]["EndSceneSeconds"], 25);
        assert_eq!(edited["Workflow"]["alpha"], true);
        assert_eq!(edited["Twitch"]["future"], original["Twitch"]["future"]);
        assert_eq!(
            edited["Twitch"]["StreamEndMode"],
            original["Twitch"]["StreamEndMode"]
        );
    }
}
#[test]
fn settings_reject_invalid_known_values_and_limits_without_defaulting_to_stop() {
    for value in [
        json!({"StreamEndMode":3}),
        json!({"StreamEndMode":"unknown"}),
        json!({"StopStreamAfterRaid":"false"}),
        json!({"SelectedRaidChannel":1}),
    ] {
        assert!(StreamEndPreferences::read(&json!({"Twitch":value})).is_err());
    }
    let mut prefs = StreamEndPreferences::default();
    prefs.selected_raid_channel = "https://twitch.tv/someone".into();
    assert!(prefs.validate().is_err());
    prefs.selected_raid_channel.clear();
    prefs.raid_start_timeout_seconds = 601;
    assert!(prefs.validate().is_err());
    assert_eq!(
        (
            retry_delay(1),
            retry_delay(2),
            retry_delay(3),
            retry_delay(4)
        ),
        (5, 8, 12, 15)
    );
}
#[test]
fn long_csharp_countdowns_remain_supported_and_lossless() {
    let original = json!({"Twitch":{"EndSceneDurationSeconds":100000,"PlannedStreamEndSeconds":1000000,"PlannedStreamEndMinutes":20000}});
    let preferences = StreamEndPreferences::read(&original).unwrap();
    assert_eq!(preferences.apply(&original).unwrap(), original);
    assert_eq!(preferences.end_scene_seconds, 100000);
    assert_eq!(preferences.planned_seconds, 1000000);
}
#[tokio::test(start_paused = true)]
async fn immediate_stop_skips_end_scene_and_restores_start_scene() {
    let (runtime, io) = setup("Immediate", 0).await;
    assert_eq!(
        *io.calls.lock().await,
        vec![
            StreamEndOperation::StopStream,
            StreamEndOperation::SetScene {
                scene: "Start".into()
            }
        ]
    );
    assert_eq!(runtime.snapshot().await.phase, "completed");
}
#[tokio::test(start_paused = true)]
async fn end_scene_waits_and_abort_keeps_stream_live() {
    let (runtime, io) = setup("EndSceneThenStop", 0).await;
    assert_eq!(runtime.snapshot().await.phase, "end_scene");
    advance(2).await;
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
    runtime.control("abort").await.unwrap();
    settle().await;
    advance(30).await;
    assert_eq!(runtime.snapshot().await.phase, "aborted");
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
}
#[tokio::test(start_paused = true)]
async fn end_scene_deadline_stops_and_music_failures_are_warnings() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    io.replies
        .lock()
        .await
        .push_back((StreamEndOperation::PauseMusic, Err("music offline".into())));
    let mut settings = plan("EndSceneThenStop");
    settings.pause_music_on_stream_end = true;
    runtime.start(settings, 0, io.clone()).await.unwrap();
    settle().await;
    advance(3).await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
    assert!(runtime
        .snapshot()
        .await
        .warnings
        .iter()
        .any(|s| s.contains("music offline")));
}
#[tokio::test(start_paused = true)]
async fn raid_starts_without_waiting_for_end_scene_but_countdown_is_not_confirmation() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    assert_eq!(runtime.snapshot().await.phase, "raid_countdown");
    assert!(io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StartRaid {
            login: "target".into()
        }));
    advance(300).await;
    assert_eq!(runtime.snapshot().await.phase, "awaiting_raid");
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
    runtime
        .observe_twitch(outgoing("own-id", "target-id"))
        .await;
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
    assert!(io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::AcknowledgeRaid));
    assert!(io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
}
#[tokio::test(start_paused = true)]
async fn incoming_unrelated_and_stale_raid_events_do_not_stop_stream() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    let mut stale = outgoing("own-id", "target-id");
    stale.received_at -= ChronoDuration::minutes(1);
    for event in [
        outgoing("incoming-id", "own-id"),
        outgoing("own-id", "other-id"),
        stale,
    ] {
        runtime.observe_twitch(event).await;
    }
    settle().await;
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
    assert!(runtime.snapshot().await.active);
}
#[tokio::test(start_paused = true)]
async fn confirmed_raid_respects_stop_and_music_options() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    let mut settings = plan("EndSceneRaidThenStop");
    settings.preferences.stop_stream_after_raid = false;
    settings.preferences.stop_music_after_raid = true;
    runtime.start(settings, 0, io.clone()).await.unwrap();
    settle().await;
    runtime
        .observe_twitch(outgoing("own-id", "target-id"))
        .await;
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
    assert!(io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::PauseMusic));
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
}
#[tokio::test(start_paused = true)]
async fn offline_target_polls_until_start_timeout_then_stops_without_raid() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    let mut settings = plan("EndSceneRaidThenStop");
    settings.preferences.raid_start_timeout_seconds = 15;
    for _ in 0..3 {
        io.replies.lock().await.push_back((
            StreamEndOperation::ProbeRaid {
                login: "target".into(),
            },
            Ok(StreamEndReply::Target(None)),
        ));
    }
    runtime.start(settings, 0, io.clone()).await.unwrap();
    settle().await;
    for _ in 0..3 {
        advance(5).await;
    }
    let calls = io.calls.lock().await;
    assert_eq!(
        calls
            .iter()
            .filter(|op| matches!(op, StreamEndOperation::ProbeRaid { .. }))
            .count(),
        3
    );
    assert!(!calls
        .iter()
        .any(|op| matches!(op, StreamEndOperation::StartRaid { .. })));
    assert!(calls.contains(&StreamEndOperation::StopStream));
    assert!(runtime
        .snapshot()
        .await
        .warnings
        .iter()
        .any(|s| s.contains("Timeout")));
}
#[tokio::test(start_paused = true)]
async fn safe_transient_start_error_retries_but_uncertain_start_never_posts_twice() {
    for error in [
        "Twitch API 429",
        "Raid-Ausgang unklar; zuerst in Twitch prüfen oder abbrechen: HTTP 503",
    ] {
        let runtime = Arc::new(StreamEndRuntime::default());
        let io = Arc::new(FakeIo::default());
        io.replies.lock().await.push_back((
            StreamEndOperation::StartRaid {
                login: "target".into(),
            },
            Err(error.into()),
        ));
        runtime
            .start(plan("EndSceneRaidThenStop"), 0, io.clone())
            .await
            .unwrap();
        settle().await;
        advance(5).await;
        let count = io
            .calls
            .lock()
            .await
            .iter()
            .filter(|op| matches!(op, StreamEndOperation::StartRaid { .. }))
            .count();
        assert_eq!(count, if error.contains("unklar") { 1 } else { 2 });
        if error.contains("unklar") {
            advance(600).await;
            assert_eq!(runtime.snapshot().await.phase, "raid_uncertain");
            assert!(!io
                .calls
                .lock()
                .await
                .contains(&StreamEndOperation::StopStream));
            runtime
                .observe_twitch(outgoing("own-id", "target-id"))
                .await;
            settle().await;
            assert_eq!(runtime.snapshot().await.phase, "completed");
        }
    }
}
#[tokio::test(start_paused = true)]
async fn abort_during_post_waits_for_result_then_cancels_and_never_stops() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    io.hold_start.store(true, Ordering::SeqCst);
    runtime
        .start(plan("EndSceneRaidThenStop"), 0, io.clone())
        .await
        .unwrap();
    settle().await;
    runtime.control("abort").await.unwrap();
    settle().await;
    assert!(runtime
        .start(plan("Immediate"), 0, io.clone())
        .await
        .is_err());
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::CancelRaid));
    io.release_start.notify_one();
    settle().await;
    assert!(io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::CancelRaid));
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
    assert_eq!(runtime.snapshot().await.phase, "aborted");
}
#[tokio::test(start_paused = true)]
async fn cancel_failure_preserves_confirmation_wait_and_cannot_claim_raid_cancelled() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    io.replies
        .lock()
        .await
        .push_back((StreamEndOperation::CancelRaid, Err("Twitch API 403".into())));
    runtime.control("cancel_raid").await.unwrap();
    settle().await;
    assert!(runtime.snapshot().await.active);
    assert!(runtime.snapshot().await.error.unwrap().contains("403"));
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
    runtime
        .observe_twitch(outgoing("own-id", "target-id"))
        .await;
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
}
#[tokio::test(start_paused = true)]
async fn raid_now_requires_ten_seconds_and_command_does_not_confirm_completion() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    assert!(runtime.control("raid_now").await.is_err());
    advance(10).await;
    runtime.control("raid_now").await.unwrap();
    settle().await;
    assert!(io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::RaidNow {
            login: "target".into()
        }));
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
}
#[tokio::test(start_paused = true)]
async fn planned_end_is_singleton_and_can_be_cancelled_or_started_early() {
    let (runtime, io) = setup("Immediate", 120).await;
    assert_eq!(runtime.snapshot().await.phase, "scheduled");
    assert!(io.calls.lock().await.is_empty());
    assert!(runtime
        .start(plan("Immediate"), 0, io.clone())
        .await
        .is_err());
    runtime.control("start_now").await.unwrap();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
    runtime
        .start(plan("Immediate"), 120, io.clone())
        .await
        .unwrap();
    settle().await;
    runtime.control("abort").await.unwrap();
    settle().await;
    advance(120).await;
    assert_eq!(runtime.snapshot().await.phase, "aborted");
    assert_eq!(
        io.calls
            .lock()
            .await
            .iter()
            .filter(|op| **op == StreamEndOperation::StopStream)
            .count(),
        1
    );
}
#[tokio::test(start_paused = true)]
async fn obs_stop_retries_three_times_and_never_pauses_or_restores_scene_after_failure() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    for _ in 0..3 {
        io.replies.lock().await.push_back((
            StreamEndOperation::StopStream,
            Err("OBS disconnected".into()),
        ));
    }
    runtime
        .start(plan("Immediate"), 0, io.clone())
        .await
        .unwrap();
    settle().await;
    advance(1).await;
    advance(1).await;
    assert_eq!(runtime.snapshot().await.phase, "error");
    assert_eq!(
        *io.calls.lock().await,
        vec![StreamEndOperation::StopStream; 3]
    );
    assert!(runtime
        .snapshot()
        .await
        .error
        .unwrap()
        .contains("OBS disconnected"));
}

#[tokio::test(start_paused = true)]
async fn abort_after_actual_completion_never_cancels_a_different_future_raid() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    io.hold_start.store(true, Ordering::SeqCst);
    runtime
        .start(plan("EndSceneRaidThenStop"), 0, io.clone())
        .await
        .unwrap();
    settle().await;
    for _ in 0..32 {
        runtime.control("abort").await.unwrap();
    }
    runtime
        .observe_twitch(outgoing("own-id", "target-id"))
        .await;
    io.release_start.notify_one();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "aborted");
    assert!(!runtime.snapshot().await.raid_pending);
    assert!(io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::AcknowledgeRaid));
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::CancelRaid));
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
}
#[tokio::test(start_paused = true)]
async fn completion_during_cancel_io_is_not_discarded() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    io.hold_cancel.store(true, Ordering::SeqCst);
    runtime.control("cancel_raid").await.unwrap();
    settle().await;
    runtime
        .observe_twitch(outgoing("own-id", "target-id"))
        .await;
    io.release_cancel.notify_one();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
    assert!(io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
}
#[tokio::test(start_paused = true)]
async fn a_slow_target_read_cannot_start_a_post_after_its_deadline() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    io.hold_probe.store(true, Ordering::SeqCst);
    let mut settings = plan("EndSceneRaidThenStop");
    settings.preferences.raid_start_timeout_seconds = 15;
    runtime.start(settings, 0, io.clone()).await.unwrap();
    settle().await;
    advance(20).await;
    io.release_probe.notify_one();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
    assert!(!io
        .calls
        .lock()
        .await
        .iter()
        .any(|op| matches!(op, StreamEndOperation::StartRaid { .. })));
}
#[tokio::test(start_paused = true)]
async fn cancelling_an_active_raid_retries_and_skip_cancels_before_obs_stop() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    runtime.control("cancel_raid").await.unwrap();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "raid_retry");
    advance(5).await;
    assert_eq!(runtime.snapshot().await.phase, "raid_countdown");
    runtime.control("skip_raid").await.unwrap();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
    let calls = io.calls.lock().await;
    assert_eq!(
        calls
            .iter()
            .filter(|op| matches!(op, StreamEndOperation::StartRaid { .. }))
            .count(),
        2
    );
    let stop = calls
        .iter()
        .position(|op| *op == StreamEndOperation::StopStream)
        .unwrap();
    assert_eq!(calls[stop - 1], StreamEndOperation::CancelRaid);
}
#[tokio::test(start_paused = true)]
async fn failed_skip_cancel_keeps_stream_live_until_actual_confirmation_or_abort() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    io.replies
        .lock()
        .await
        .push_back((StreamEndOperation::CancelRaid, Err("network error".into())));
    runtime.control("skip_raid").await.unwrap();
    settle().await;
    advance(300).await;
    assert!(runtime.snapshot().await.active);
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
    runtime.control("abort").await.unwrap();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "aborted");
}
#[tokio::test(start_paused = true)]
async fn cancel_failure_during_abort_retains_pending_guard_and_visible_warning() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    io.replies
        .lock()
        .await
        .push_back((StreamEndOperation::CancelRaid, Err("network error".into())));
    runtime.control("abort").await.unwrap();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "aborted");
    assert!(runtime.snapshot().await.raid_pending);
    assert!(runtime
        .snapshot()
        .await
        .warnings
        .iter()
        .any(|s| s.contains("in Twitch prüfen")));
    assert!(runtime
        .start(plan("Immediate"), 0, io.clone())
        .await
        .is_err());
}
#[tokio::test(start_paused = true)]
async fn login_fallback_is_supported_but_cannot_override_conflicting_ids() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    let mut login_only = outgoing("own-id", "target-id");
    login_only.data.remove("from_broadcaster_user_id");
    login_only.data.remove("to_broadcaster_user_id");
    login_only
        .data
        .insert("to_broadcaster_user_login".into(), "TARGET".into());
    runtime
        .observe_twitch(outgoing("other-id", "target-id"))
        .await;
    settle().await;
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
    runtime.observe_twitch(login_only).await;
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
}
#[tokio::test(start_paused = true)]
async fn retransmitted_old_server_timestamp_does_not_become_a_new_completion_proof() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    let mut replay = outgoing("own-id", "target-id");
    replay.data.insert(
        "eventSubMessageTimestamp".into(),
        (Utc::now() - ChronoDuration::minutes(1)).to_rfc3339(),
    );
    runtime.observe_twitch(replay).await;
    settle().await;
    let mut invalid = outgoing("own-id", "target-id");
    invalid.data.insert(
        "eventSubMessageTimestamp".into(),
        "invalid timestamp".into(),
    );
    runtime.observe_twitch(invalid).await;
    settle().await;
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
    let mut fresh = outgoing("own-id", "target-id");
    fresh
        .data
        .insert("eventSubMessageTimestamp".into(), Utc::now().to_rfc3339());
    runtime.observe_twitch(fresh).await;
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
}

#[tokio::test(start_paused = true)]
async fn unknown_mutation_error_is_uncertain_and_never_retried_as_a_second_post() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    io.replies.lock().await.push_back((
        StreamEndOperation::StartRaid {
            login: "target".into(),
        },
        Err("network connection interrupted".into()),
    ));
    runtime
        .start(plan("EndSceneRaidThenStop"), 0, io.clone())
        .await
        .unwrap();
    settle().await;
    advance(600).await;
    assert_eq!(runtime.snapshot().await.phase, "raid_uncertain");
    assert_eq!(
        io.calls
            .lock()
            .await
            .iter()
            .filter(|op| matches!(op, StreamEndOperation::StartRaid { .. }))
            .count(),
        1
    );
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
}
#[test]
fn editing_mode_retains_enum_encoding_and_remembers_normalized_raid_targets() {
    for original_mode in [json!(1), json!("EndSceneThenStop")] {
        let original = json!({"Twitch":{"StreamEndMode":original_mode,"RaidChannels":["other","OTHER"],"Unknown":[1,2]},"Workflow":{"Unknown":true}});
        let mut draft = StreamEndPreferences::read(&original).unwrap();
        draft.mode = "EndSceneRaidThenStop".into();
        draft.selected_raid_channel = " @target ".into();
        let edited = draft.apply(&original).unwrap();
        assert_eq!(
            edited["Twitch"]["StreamEndMode"],
            if original["Twitch"]["StreamEndMode"].is_number() {
                json!(2)
            } else {
                json!("EndSceneRaidThenStop")
            }
        );
        assert_eq!(edited["Twitch"]["SelectedRaidChannel"], "target");
        assert_eq!(edited["Twitch"]["RaidChannels"], json!(["target", "other"]));
        assert_eq!(edited["Twitch"]["Unknown"], json!([1, 2]));
        assert_eq!(edited["Workflow"], original["Workflow"]);
        assert_eq!(draft.planned_mode(), "EndSceneThenStop");
        draft.raid_on_stream_end = true;
        assert_eq!(draft.planned_mode(), "EndSceneRaidThenStop");
    }
}
#[tokio::test(start_paused = true)]
async fn invalid_or_self_raid_plan_rejects_before_any_io_and_updates_are_observable() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    let mut draft = plan("EndSceneRaidThenStop");
    draft.broadcaster_id.clear();
    assert!(runtime.start(draft, 0, io.clone()).await.is_err());
    let mut draft = plan("EndSceneRaidThenStop");
    draft.preferences.selected_raid_channel = "OWN".into();
    assert!(runtime.start(draft, 0, io.clone()).await.is_err());
    assert!(io.calls.lock().await.is_empty());
    let mut events = runtime.subscribe_changes();
    let run = runtime
        .start(plan("EndSceneThenStop"), 0, io.clone())
        .await
        .unwrap();
    settle().await;
    assert_eq!(events.recv().await.unwrap().run_id, run.run_id);
    assert!(runtime.control("invalid").await.is_err());
    runtime.control("skip_end").await.unwrap();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
    assert!(runtime.control("abort").await.is_err());
}

#[tokio::test(start_paused = true)]
async fn abort_after_a_definite_rejected_post_does_not_cancel_an_unrelated_raid() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    io.hold_start.store(true, Ordering::SeqCst);
    io.replies.lock().await.push_back((
        StreamEndOperation::StartRaid {
            login: "target".into(),
        },
        Err("Twitch API 429".into()),
    ));
    runtime
        .start(plan("EndSceneRaidThenStop"), 0, io.clone())
        .await
        .unwrap();
    settle().await;
    runtime.control("abort").await.unwrap();
    io.release_start.notify_one();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "aborted");
    assert!(!runtime.snapshot().await.raid_pending);
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::CancelRaid));
}
#[tokio::test(start_paused = true)]
async fn abort_while_successful_cancel_is_in_flight_does_not_cancel_twice() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    io.hold_cancel.store(true, Ordering::SeqCst);
    runtime.control("cancel_raid").await.unwrap();
    settle().await;
    runtime.control("abort").await.unwrap();
    io.release_cancel.notify_one();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "aborted");
    assert!(!runtime.snapshot().await.raid_pending);
    assert_eq!(
        io.calls
            .lock()
            .await
            .iter()
            .filter(|op| **op == StreamEndOperation::CancelRaid)
            .count(),
        1
    );
    assert!(!io
        .calls
        .lock()
        .await
        .contains(&StreamEndOperation::StopStream));
}

#[tokio::test(start_paused = true)]
async fn external_obs_stop_aborts_planning_and_raid_without_another_stop_request() {
    for mode in ["Immediate", "EndSceneRaidThenStop"] {
        let (runtime, io) = setup(mode, if mode == "Immediate" { 60 } else { 0 }).await;
        runtime.observe_obs_stopped().await;
        settle().await;
        assert_eq!(runtime.snapshot().await.phase, "aborted");
        assert!(runtime.snapshot().await.status.contains("außerhalb"));
        assert!(!io
            .calls
            .lock()
            .await
            .contains(&StreamEndOperation::StopStream));
    }
}
#[tokio::test(start_paused = true)]
async fn unresolved_idle_raid_can_be_cleared_only_after_explicit_external_resolution() {
    let (runtime, io) = setup("EndSceneRaidThenStop", 0).await;
    assert!(runtime.resolve_pending_raid().await.is_err());
    io.replies
        .lock()
        .await
        .push_back((StreamEndOperation::CancelRaid, Err("Twitch API 403".into())));
    runtime.control("abort").await.unwrap();
    settle().await;
    assert!(runtime.snapshot().await.raid_pending);
    runtime.resolve_pending_raid().await.unwrap();
    runtime
        .start(plan("Immediate"), 0, io.clone())
        .await
        .unwrap();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "completed");
}

#[tokio::test(start_paused = true)]
async fn queued_abort_is_visible_until_the_mutation_settles_and_cleanup_finishes() {
    let runtime = Arc::new(StreamEndRuntime::default());
    let io = Arc::new(FakeIo::default());
    io.hold_start.store(true, Ordering::SeqCst);
    runtime
        .start(plan("EndSceneRaidThenStop"), 0, io.clone())
        .await
        .unwrap();
    settle().await;
    runtime.control("abort").await.unwrap();
    assert_eq!(
        runtime.snapshot().await.pending_action.as_deref(),
        Some("abort")
    );
    assert!(runtime.snapshot().await.active);
    io.release_start.notify_one();
    settle().await;
    assert_eq!(runtime.snapshot().await.phase, "aborted");
    assert_eq!(runtime.snapshot().await.pending_action, None);
}
