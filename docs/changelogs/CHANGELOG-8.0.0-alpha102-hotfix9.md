# Creator Control Suite 8.0.0 Alpha 102 – Hotfix 9

## Overlay-Datenpfad und konkurrierende Writer behoben

- DenverJohn-v18.x-Overlays werden anhand von `Overlay/modules/ui` erkannt.
- Für diese Struktur ist ausschließlich `Overlay/data/overlay-data.json` autoritativ.
- Alte Einstellungen auf `data/overlay-data.json` können den tatsächlichen HTML-Pfad nicht mehr überschreiben.
- Der Overlay-Import verknüpft DenverJohn-Projekte nun im richtigen Ordner `Overlay/data`.
- Spotify, Live, Twitch, OBS und der zentrale OverlayDataService verwenden eine gemeinsame prozessweite Schreibsperre.
- Read-Modify-Write-Vorgänge können sich dadurch nicht mehr gegenseitig mit älteren Zuständen überschreiben.
- Laufzeitupdates ersetzen die JSON nicht mehr per `File.Move`, sondern schreiben in-place; Hardlinks bleiben erhalten.
