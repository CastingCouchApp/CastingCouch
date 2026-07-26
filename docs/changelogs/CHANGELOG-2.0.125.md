# Creator Control Suite 2.0.125

## Compile consistency recovery
- Fixed constructor nullable-flow errors by assigning injected services before event-handler lambdas capture them.
- Restored the missing PrepareStreamAsync command bridge to the existing configured-service preparation workflow.
- Added RegisterTwitchEventAsync to IStreamWorkflowService to match the existing implementation.
- Added the missing WPF media namespace for SolidColorBrush, Color and Brushes.
- Corrected overlay goal updates to use FollowerGoalState and SubGoalState instead of legacy scalar properties.
- Added EndSceneDurationSeconds to TwitchSettings with a 60-second default.
- Added the missing Twitch raid duration/profile controls to the Services page.
- Added the missing Spotify progress bar and time labels to the dashboard.
- Replaced stale preflight references with the active OBS client, Twitch snapshot and Streamer.bot WebSocket state.
