# Creator Control Suite 8.0.0-alpha39

## Spotify-Automationen – Ablaufzeiten gespeicherter Zustände

- Jeder gesicherte Spotify-Wiedergabezustand erhält jetzt einen UTC-Zeitstempel.
- Einzelansicht und zentrale Übersicht zeigen an, wie alt ein Zustand ist.
- Die maximale Gültigkeitsdauer ist im Automationseditor zwischen 1 und 10080 Minuten einstellbar.
- Abgelaufene Zustände werden in der Übersicht deutlich mit `[ABGELAUFEN]` markiert.
- Abgelaufene Zustände bleiben bewusst manuell wiederherstellbar, bis sie verworfen werden.
- Neuer Befehl `ABGELAUFENE BEREINIGEN` entfernt ausschließlich veraltete Zustände.
- Übersicht und Diagnose melden Anzahl und Gruppen der bereinigten Zustände.
