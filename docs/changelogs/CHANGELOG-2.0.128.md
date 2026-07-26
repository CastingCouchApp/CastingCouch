# Creator Control Suite 2.0.129

## Spotify preflight API consistency
- Fixed the dashboard preflight check to use the existing Spotify snapshot API instead of a non-existent `SpotifyModule.IsConnected` property.
- Spotify connection state is now derived from `SpotifyModule.GetSnapshot().Authenticated`, matching the module's public contract and the Twitch preflight pattern.
- Retains the Windows path-length build fix and all compile consistency fixes from 2.0.127.
