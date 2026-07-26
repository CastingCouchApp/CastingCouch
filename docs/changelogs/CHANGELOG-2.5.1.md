# Creator Control Suite 2.5.1

## Overlay-Szenen direkt aus der Suite anlegen

- Neuer Button **+ SZENE HINZUFÜGEN** in der Overlay-Projektbibliothek.
- Frei definierbarer Szenenname.
- Mehrfachauswahl geeigneter Dateien: HTML, Bilder, Videos, Audio, CSS, JavaScript, JSON und Schriftdateien.
- Dateien werden in `scenes/<Szenenname>/` des ausgewählten Overlay-Projekts kopiert.
- Die `overlay.json` wird automatisch ergänzt und gespeichert.
- Bei aktiver OBS-Verbindung wird die Szene sofort angelegt.
- HTML wird als Browserquelle, Bilder als Bildquelle und Video/Audio als Medienquelle angelegt.
- CSS, JavaScript, JSON und Fonts werden als Projekt-Assets gespeichert, aber nicht als eigene OBS-Quelle angelegt.
- Ist OBS nicht verbunden, bleiben die Elemente gespeichert und werden beim nächsten Synchronisieren übernommen.
