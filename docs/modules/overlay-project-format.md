# Overlay-Projekte

Ein vorhandener HTML-Ordner kann ohne Manifest importiert werden. Die Suite scannt rekursiv alle `*.html`-Dateien. Optional beschreibt eine `overlay.json` Namen, Auflösung, Elemente und OBS-Zuordnungen.

```json
{
  "name": "Denver Overlay",
  "version": "1.0",
  "author": "Alan",
  "width": 1920,
  "height": 1080,
  "items": [
    {
      "name": "Start",
      "kind": "Scene",
      "relativePath": "scenes/start.html",
      "obsScene": "Start",
      "obsSource": "ccs_denver_start",
      "enabled": true
    },
    {
      "name": "Spotify",
      "kind": "Module",
      "relativePath": "modules/spotify.html",
      "obsScene": "Game",
      "obsSource": "ccs_denver_spotify",
      "enabled": true
    }
  ]
}
```

Die Originaldateien bleiben an ihrem Speicherort. Im lokalen Suite-Katalog werden nur Projektinformationen und OBS-Zuordnungen gespeichert. „Aus OBS übernehmen“ liest vorhandene Browserquellen samt lokaler Datei oder URL ein. „Mit OBS synchronisieren“ erstellt fehlende Szenen/Browserquellen oder aktualisiert bestehende Quellen.


## Automatische Manifest-Erstellung (2.4.5)

Beim Import eines HTML-Ordners und beim Übernehmen von Browserquellen aus OBS erzeugt die Suite automatisch eine `overlay.json`. Existiert bereits eine Datei, wird sie gelesen und bei Änderungen aktualisiert. Unter **Einstellungen → Allgemein** kann der aktive Pfad angezeigt, geändert oder eine eigene JSON-Datei ausgewählt werden. Wird eine externe JSON als Vorlage gewählt, legt die Suite beim Import eine normalisierte `overlay.json` im jeweiligen Overlay-Projektordner an, damit das Projekt portabel bleibt.
