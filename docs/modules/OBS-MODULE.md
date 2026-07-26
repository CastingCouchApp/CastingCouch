# OBS-Modul – 2.0.81

## Implementiert

- OBS WebSocket 5.x
- Standardport 4455
- Hello/Identify/Identified-Handshake
- SHA-256-Authentifizierung
- dauerhafte Receive-Schleife
- parallele Request-Zuordnung über Request-ID
- Request-Timeouts
- Verbindung und Trennung
- GetVersion
- GetSceneList
- GetInputList
- GetCurrentProgramScene
- SetCurrentProgramScene
- GetStreamStatus
- StartStream
- StopStream
- CurrentProgramSceneChanged-Event
- WPF-Bedienung im OBS-Einstellungsbereich
- automatische Verbindung optional
- Passwort über DPAPI

## Sicherheitsverhalten

- Das OBS-Passwort steht nicht in settings.json.
- Streamstart und Streamende benötigen eine Bestätigung.
- Request- und Verbindungs-Timeouts verhindern festhängende UI-Vorgänge.

## Nächste Erweiterung

- Audioquellen und Mute
- Browserquellen
- Scene Items
- native Media Sources
- Recording und Replay Buffer
- automatische Wiederverbindung
