# Creator Control Suite 8.0.0-alpha73

## IPC-Shutdown-Race behoben

- Die Named-Pipe-Annahmeschleife wird jetzt zuerst vollständig beendet.
- Erst danach werden die noch registrierten Client-Aufgaben erfasst und abgewartet.
- Eine während des Shutdowns gerade angenommene Verbindung kann nicht mehr aus der Aufgaben-Momentaufnahme herausfallen.
- Die interne Zustandsbereinigung läuft nun auch bei Timeout, Abbruch oder Ausnahme in einem `finally`-Block.
- Noch laufende Client-Aufgaben werden bei einem Timeout nicht mehr künstlich aus der Nachverfolgung gelöscht.
- Version auf `8.0.0-alpha73` aktualisiert.
