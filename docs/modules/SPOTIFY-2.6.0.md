# Spotify request management 2.6.0

- Spotify Web API requests are serialized through a single request gate.
- While Spotify's Retry-After cooldown is active, no additional HTTP requests are sent.
- The dashboard refreshes only the current playback state every five seconds.
- Devices and playlists are cached and refreshed at most every five minutes unless the user explicitly refreshes.
- Player actions trigger only one delayed playback refresh instead of a complete four-endpoint refresh.
- The remaining cooldown time is updated once per second and clears automatically.
- Spotify API diagnostics remain available under Protokolle / Spotify.Api.
