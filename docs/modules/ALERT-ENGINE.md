# Alert Engine – 2.0.81

## Architektur

Die Alert Engine verwendet keine Browser-Videos.

OBS erhält zwei native Quellen:

- `ccs_alert_media` – native OBS Media Source
- `ccs_alert_text` – native OBS Text Source

Beide Quellen liegen in der konfigurierbaren Szene `_alerts`.

Die Szene und Quellen werden **nicht** automatisch beim Suite-Start oder beim Alert-Playback angelegt. Unter Alerts → OBS Alert Quellen muss **IN OBS ALS SZENE HINZUFÜGEN** einmalig ausgeführt werden.

## Queue

- bounded Channel
- ein Reader
- mehrere Producer
- feste Reihenfolge
- Queue-Limit
- Zwischenpause
- alter Alert wird vor dem nächsten vollständig gestoppt
- Media Source wird mit STOP beendet
- Quellen werden nach dem Alert deaktiviert

Damit wird verhindert:

- MP4-Endlosschleife
- alter Sound vor neuem Alert
- zwei Videos gleichzeitig
- Browser-/CEF-Crashes
- wiederholtes Browser-Reloading

## Twitch-Verknüpfung

Folgende Events werden automatisch eingereiht:

- Follow
- Sub
- ReSub
- GiftSub
- Cheer
- Raid

## Designer

Konfigurierbar:

- Textvorlage
- MP4/Medienpfad
- Soundpfad
- Dauer
- Priorität
- Schrift
- Schriftgröße
- Schriftfarbe
- Animation
- OBS-Szene
- OBS-Medienquelle
- OBS-Textquelle
- Zwischenpause

## OBS-Setup

1. Alert im Designer anlegen/speichern
2. Optional Szene-/Quellennamen unter „OBS Alert Quellen“ anpassen
3. **IN OBS ALS SZENE HINZUFÜGEN** klicken (OBS muss verbunden sein)
4. Danach können „In OBS testen“ und Live-Events die vorhandenen Quellen nutzen

Fehlen Szene oder Quellen, erscheint ein klarer Hinweis statt automatischer Neuanlage.

## Vorschau

Die Vorschau läuft innerhalb der Suite.

Der OBS-Test legt denselben Alert in die echte Queue.

## Noch offen

- separate Audio Engine / Audio-Endpunktwahl
- echte Timeline-Animationen
- Drag-and-Drop-Positionierung
- mehrere Ebenen
- GIF/Lottie/SVG
- Theme-Pakete
- Prioritätsqueue statt FIFO
