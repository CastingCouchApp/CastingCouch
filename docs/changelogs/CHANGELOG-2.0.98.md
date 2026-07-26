# Creator Control Suite 2.0.98

## Expanded Stream Deck / local IPC control
- Added direct OBS stream start and stop commands.
- Added OBS input mute/unmute command with input and muted arguments.
- Added Spotify play, pause, next and previous commands.
- Added Spotify volume command with a 0-100 volume argument.
- Added dedicated 25% and 50% Spotify volume actions for the generated default Stream Deck profile.
- Expanded the generated default Stream Deck profile with stream start/stop and Spotify controls.
- Existing workflow commands for prepare, countdown, live, pause, resume and end remain available.
- Existing arbitrary OBS scene command remains available.

## New IPC commands
- stream.start
- stream.stop
- obs.mute input=<name> muted=true|false
- spotify.play
- spotify.pause
- spotify.next
- spotify.previous
- spotify.volume volume=0..100
- spotify.volume25
- spotify.volume50
