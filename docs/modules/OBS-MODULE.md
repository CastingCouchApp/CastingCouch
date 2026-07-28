# OBS-Modul

## Vertrag und Aufbau

- OBS WebSocket 5.x auf dem konfigurierbaren Standardport 4455
- `Hello`/`Identify`/`Identified`-Handshake mit RPC-Version 1
- SHA-256-Challenge-Response-Authentifizierung
- zentraler JSON-Codec mit einem Payload-Limit von 4 MiB
- dauerhafte Receive-Schleife und parallele Zuordnung über Request-ID
- Request- und Verbindungs-Timeouts
- getrennte Dateien für Transport, Protokoll, Requests und Operationen

Fixture-basierte Contract-Tests prüfen authentifizierten Handshake,
RPC-Aushandlung, Request-/Response-Korrelation, Erfolgs- und Fehlerstatus,
Szenen- und Pegel-Events sowie ungültige und zu große Frames. Unbekannte
zukünftige Felder bleiben kompatibel.

## Fähigkeiten

- Szenen, Szenenobjekte und Übergänge
- Audioquellen, Mute, Pegel, Monitoring und Sync-Offset
- Streaming, Recording, Replay Buffer und virtuelle Kamera
- Inputs, Filter, Browser-, Text- und Media-Quellen
- Snapshots, Status- und Statistikabfragen
- Programm-Szenen-, Struktur- und Live-Pegel-Events
- optionale automatische Verbindung über die WPF-Einstellungen

## Sicherheitsverhalten

- Das OBS-Passwort steht nicht in settings.json.
- Streamstart und Streamende benötigen eine Bestätigung.
- Request- und Verbindungs-Timeouts verhindern festhängende UI-Vorgänge.
- Übergroße oder strukturell ungültige Frames werden vor der Verarbeitung
  abgewiesen.

## Offene Freigaben

- Windows-Integrationstest mit einer realen unterstützten OBS-Version
- Abbruch-, Reconnect- und Langzeittest im 24-Stunden-Soak
