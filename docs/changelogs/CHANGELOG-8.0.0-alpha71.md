# Creator Control Suite 8.0.0-alpha71

## IPC-Anfrage-Timeout

- Einzelne Named-Pipe-Verbindungen können den IPC-Server nicht mehr unbegrenzt blockieren.
- Für das Einlesen und Ausführen einer IPC-Anfrage gilt nun ein Zeitlimit von fünf Sekunden.
- Ein Anfrage-Timeout beendet nur die betroffene Verbindung; der Accept-Loop läuft weiter.
- Bereits vom Client geschlossene Verbindungen werden beim Schreiben der Antwort toleriert.
- Timeout-Ereignisse werden im Anwendungslog protokolliert.
