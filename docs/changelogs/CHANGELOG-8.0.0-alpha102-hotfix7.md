# Creator Control Suite 8.0.0-alpha102 Hotfix 7

## Spotify-Overlay wieder sichtbar

- Der im Spotify-Bereich ausgewählte `overlay-data.json`-Pfad wird nun von allen Spotify-Laufzeitroutinen verbindlich verwendet.
- Die Suite leitet den Zielpfad nicht mehr eigenmächtig erneut aus dem Overlay-Hauptordner ab.
- `spotify.connected` bleibt bei vorhandenem Playback/Titel wahr, auch wenn der Tokenstatus während Start oder Refresh kurzzeitig noch nicht gesetzt ist.
- Diagnoseprotokoll ergänzt: Zielpfad, Verbindungsstatus, Sichtbarkeit und Titel werden bei jedem Overlay-Update protokolliert.
- Die Sichtbarkeitsstabilisierung aus Hotfix 6 bleibt erhalten.
