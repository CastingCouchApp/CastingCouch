# Overlay-System – 2.0.81

Die Suite schreibt atomar nach:

`<OverlayRoot>\data\overlay-data.json`

Bereiche: stream, twitch, spotify, obs, alerts, stats und branding.

Mitgeliefert werden Startszene, Live-Status, Follower-Ziel,
Spotify-Widget und Endstatistik. Die HTML-Dateien lesen ausschließlich Daten
und steuern weder OBS noch den Stream-Workflow.

## Streamer-HUD (nur lokal)

Zusätzlich gibt es ein **persönliches Streamer-HUD**: ein transparentes TopMost-WPF-Fenster
über dem gewählten Game-Monitor (Chat, Events, Live-Status).

- **Nicht** für den Stream gedacht — getrennt von OBS-Browserquellen.
- Per `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` vom Capture ausgeschlossen
  (relevant vor allem bei Display Capture / Screenshare).
- Keine Injection in Spieleprozesse; externes Fenster wie bei vielen Overlay-Tools.
- Keine Anticheat-Garantie; bei Exclusive Fullscreen kann das HUD überdeckt werden —
  Borderless Windowed empfohlen.

Konfiguration: Overlay-Seite → „Streamer-HUD (nur lokal)“.
