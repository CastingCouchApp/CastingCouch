# Creator Control Suite 2.0.144

## Dashboard live-data refresh block
- Added a resilient 5-second dashboard live refresh loop.
- OBS state, current scene and stream state refresh automatically while connected.
- Twitch viewer sample, followers, goals, users and connection state refresh automatically while authenticated.
- Spotify playback state refreshes automatically while authenticated.
- Streamer.bot top dashboard status is synchronized every refresh cycle.
- Viewer trend receives zero samples when offline so the chart reflects stream drop-off instead of freezing.
- Current OBS scene selection follows the real OBS program scene.
- Automation summary is rebuilt from the current workflow, raid and dashboard automation settings.
- Manual disconnects now update the top OBS/Twitch/Spotify status labels consistently.
- An immediate live refresh runs once when the main window finishes loading.
