# Creator Control Suite 2.0.93

## Workflow and history
- Stream vorbereiten now finishes with the dashboard preflight check.
- Stream start can switch to the configured start scene and then to the configured live scene.
- Stream end switches to the configured end scene and waits the configured end-scene duration before stopping OBS.
- Workflow progress is shown in the dashboard.
- Completed stream sessions are persisted locally as JSON Lines.
- Dashboard stream history shows the latest 50 sessions with duration, peak viewers, average viewers and follower gain.
- Stream history folder can be opened directly from the dashboard.
- Workflow events are also written to the dashboard notification center.

## Note
The existing raid-target inspection remains active. Automatic execution of a Twitch raid still depends on the Twitch raid command implementation and is not falsely marked as completed here.
