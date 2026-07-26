# Migration – 2.0.81

Die automatische Suche prüft typische Ordner unter:

- Dokumente
- LocalAppData
- AppData

Erkannt werden:

- settings.json
- overlay-data.json
- content-Ordner
- alerts-Ordner

Importiert werden soweit möglich:

- OBS Host und Port
- Twitch-Kanal
- Overlay-Pfad
- Szenennamen
- Dauer der Endszene

Tokens werden aus Sicherheitsgründen nicht aus unsicheren Legacy-Dateien
übernommen.
