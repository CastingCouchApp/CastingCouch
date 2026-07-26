# Creator Control Suite 2.0.147

## Dashboard action safety
- Added a common guarded executor for dashboard async actions.
- Prevents rapid double-clicks and parallel execution on stream, service, scene and Spotify controls.
- The active button is temporarily disabled while the action is running.
- The button displays a temporary progress ellipsis and restores its original label afterwards.
- Errors are routed into the dashboard notification center.
- Dashboard live data refreshes automatically after successful actions.
- Service button labels are re-synchronized after every guarded action.
- Test Alert is also guarded against accidental repeated clicks.
- Added tooltips to the most important dashboard actions.
