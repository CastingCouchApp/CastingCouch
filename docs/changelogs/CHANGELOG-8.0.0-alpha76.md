# Creator Control Suite 8.0.0-alpha76

## Dienstintegrationen abgeschlossen und abgesichert

Diese Version fasst den vollständigen Integrationsstand der vier zentralen Streaming-Dienste zusammen und schützt ihn durch eine neue Build-Prüfung.

### OBS
- Szenenwechsel, Quellenverwaltung und Sichtbarkeit
- Audiomixer mit Mute, Lautstärke, Gruppen und Audioprofilen
- Übergänge, Filter und Szenen-Transformationen
- Stream, Aufnahme, Replay Buffer und virtuelle Kamera
- Profile, Szenensammlungen und zeitgesteuerte Quellen

### Streamer.bot
- WebSocket-Verbindung und automatische Wiederverbindung
- Laden und Ausführen vorhandener Aktionen
- JSON-Argumente, Diagnose und Ausführungshistorie
- Event-Listener für Alerts und Spotify-Ducking
- Aktivieren und Deaktivieren konfigurierter Streamer.bot-Alerts

### Spotify
- Wiedergabesteuerung, Lautstärke, Shuffle und Wiederholung
- Geräteauswahl und Übertragung der Wiedergabe
- Playlists, Titelsuche, Warteschlange und Verlauf
- Overlay-JSON, OBS-Mute-Erkennung und Alert-Ducking

### Twitch
- Chat, Chatter-Liste und Eventanzeige
- Titel- und Kategorieänderung
- Raid-Ziele, Statusprüfung, Raid-Start und Abbruch
- Kanalpunkte, Umfragen und Predictions

### Qualitätssicherung
- Neue Prüfung `build/Test-ServiceIntegrationCompleteness.ps1`
- Die Prüfung läuft automatisch im Build-Preflight.
- Fehlende API-Methoden, UI-Elemente oder Event-Verknüpfungen brechen den Preflight künftig mit einer eindeutigen Meldung ab.
