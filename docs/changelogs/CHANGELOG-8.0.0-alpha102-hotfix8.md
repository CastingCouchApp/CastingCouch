# Creator Control Suite 8.0.0-alpha102 Hotfix 8

## Overlay-Datenverbindung stabilisiert

- Zentralen Fehler in der Overlay-Datenpipeline behoben.
- `overlay-data.json` wird nicht mehr per atomarem Dateiaustausch ersetzt.
- Bestehende Hardlinks und symbolische Links bleiben beim Schreiben erhalten.
- Spotify-, Live- und sonstige Overlay-Module lesen dadurch dauerhaft denselben Datenstand.
- Jeder Schreibvorgang verwendet eine eigene temporäre Datei.
- Temporäre Dateien werden auch nach Fehlern bereinigt.
- Bereits getrennte Datenverknüpfungen werden beim Laden beziehungsweise Synchronisieren eines Overlay-Projekts erneut verbunden.

## Ursache

Frühere Versionen erzeugten für importierte Overlays einen Hardlink zur zentralen `overlay-data.json`, ersetzten die zentrale Datei beim Aktualisieren jedoch mit `File.Move`. Das löste den Hardlink. OBS-Quellen konnten anschließend abwechselnd alte und neue Dateien lesen.
