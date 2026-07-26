# 8.0.0-alpha102 Hotfix 16

- Live-Status setzt bei jedem erkannten aktiven OBS-Stream garantiert `stream.isLive=true` und eine nichtleere `stream.startedAt`-Zeit.
- Bei außerhalb der Suite gestarteten Streams wird die Startzeit aus dem OBS-Output-Timecode rekonstruiert; andernfalls wird der erste Erkennungszeitpunkt verwendet.
- Die ausgewählte Spotify-Startplaylist wird nun auch bei einem Streamstart über OBS, Streamer.bot oder andere Steuerwege ausgelöst.
- Pro Streamsession wird die Startplaylist höchstens einmal automatisch gestartet.
- Beim bestätigten Streamende werden Session-Startzeit und Playlist-Startmarkierung zurückgesetzt.
- Fehler beim automatischen Playliststart werden in Protokollen und Dashboard mit der tatsächlichen Fehlermeldung angezeigt.
