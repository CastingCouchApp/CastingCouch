# Creator Control Suite 2.0.94

## Automatic Twitch raid workflow
- Added Twitch Helix Start Raid integration using the existing channel:manage:raids scope.
- Raid target is resolved by login immediately before the raid.
- The raid is prevented when the target does not exist, is the broadcaster's own channel, or is offline.
- Raid preview/status now includes the target's current viewer count in addition to online state, category and stream title.
- Stream-end workflow now checks the configured raid target after the end-scene countdown, starts the raid when valid and then stops the OBS stream.
- Raid progress and failures are written to the dashboard workflow status and notification center.

## API
- ITwitchApiClient now exposes StartRaidAsync.
- TwitchModule exposes StartRaidAsync for the connected broadcaster.
- TwitchRaidTargetStatus now includes ViewerCount.
