# Creator Control Suite 2.0.111

## Automatic connection watchdog
- Added background connection monitoring for OBS, Twitch, Spotify and Streamer.bot.
- Lost connections can be restored automatically without user interaction.
- Reconnect behavior can be enabled or disabled globally.
- Each service can be included or excluded individually.
- Watchdog interval is configurable from 5 to 300 seconds.
- Added reconnect cooldown to prevent rapid reconnect loops.
- Connection losses and successful automatic reconnects are written to the persistent Notification Center.
- Reconnect attempts use the existing silent connection methods where available, avoiding repeated modal error dialogs.
- Watchdog failures are written to application logs without interrupting the active stream.
