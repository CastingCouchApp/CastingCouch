# Creator Control Suite 2.9.8

## Behoben

- Beim Import eines neuen Overlay-Ordners wird nicht mehr die zuvor aktive `overlay.json` eines anderen Projekts verwendet.
- Die HTML-Dateien werden immer aus dem tatsächlich ausgewählten Ordner eingelesen.
- Veraltete lokale Browser-Einträge einer falschen `overlay.json` werden entfernt.
- Neue `.html`- und `.htm`-Dateien im ausgewählten Ordner werden automatisch ergänzt.
- Doppelte Projekt-IDs aus kopierten Manifesten werden erkannt und neu vergeben.
- Die zentrale `overlay-data.json` bleibt weiterhin die gemeinsame Live-Datenquelle aller Projekte.
- Konstruktoraufruf des OverlayProjectService an die zentrale Overlay-Datenquelle angepasst.
