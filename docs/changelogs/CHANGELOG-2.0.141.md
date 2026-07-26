# Creator Control Suite 2.0.141

## Exact fixed dashboard hierarchy correction
- The first visible row is now the compact service/status strip for Stream, OBS, Twitch, Spotify, Streamer.bot and Alerts.
- A dynamic Stream starten / Stream beenden action and a dedicated Settings button sit directly to the right of the status strip.
- The old dashboard title/action header is hidden so the service row is truly the top row.
- Main row: Stream Status | Current OBS Scene | Quick Access.
- Second row: Services in a fixed 3x2 grid | full Spotify player | Twitch Chat.
- Third row: Next Automations | Live Events | System Resources.
- Restored the full Spotify card including album cover, track/album, progress, playback controls, playlist and volume controls.
- Increased the default window to 1760x1040 with a 1480 minimum width.
- The top stream action dynamically switches between Start and End based on the real OBS stream state.
- The old movable-dashboard layout engine no longer reparents the fixed dashboard controls.
