# Creator Control Suite 3.2.3

## OBS-Startbereitschaft beim Stream vorbereiten

- Nach dem automatischen Start von OBS wartet die Suite nun, bis OBS nicht nur per WebSocket verbunden ist, sondern auch Szenenabfragen akzeptiert.
- Der OBS-Fehler 207 „OBS is not ready to perform the request“ wird beim Start nicht mehr sofort als Fehlerdialog angezeigt.
- Der Wechsel auf die konfigurierte Startszene wird bei einem vorübergehenden OBS-Startfehler automatisch wiederholt.
- Der Fortschrittsbereich zeigt währenddessen „OBS wird vorbereitet“ einschließlich der aktuellen Versuchsnummer an.
- Erst wenn OBS nach rund 20 Sekunden weiterhin nicht bereit ist, wird eine verständliche Fehlermeldung angezeigt.
