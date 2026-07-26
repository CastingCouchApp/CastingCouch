# Creator Control Suite 2.0.143

## Existing dashboard functions fully wired
- Dashboard OBS connect now uses the real OBS connection flow and refreshes the full OBS/dashboard state immediately.
- Dashboard Twitch connect now uses the real Twitch flow and refreshes users, viewer sample, followers and goals.
- Dashboard Spotify connect now uses the real Spotify connection and refresh flow.
- Dashboard Streamer.bot connect now updates both the services page and the top dashboard status indicator.
- Streamer.bot disconnect also updates the dashboard status indicator.
- OBS scene switching now refreshes the OBS snapshot and current-scene dashboard immediately.
- Stream Deck dashboard action now opens the existing StreamDeck local-data/configuration folder.
- Removed the fake Spotify Shuffle notification. The button is explicitly disabled until the current Spotify module exposes a real shuffle API.
- Existing real functions remain connected for stream preparation, stream start/end, test alert, overlay access, Spotify playback, Twitch chat and system diagnostics.
