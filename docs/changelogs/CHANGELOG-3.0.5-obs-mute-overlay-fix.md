# Creator Control Suite 3.0.5

- Liest den Mute-Status der OBS-Mikrofonquelle aus.
- Liest den Mute-Status der OBS-Broadcast-/Desktop-Audioquelle aus.
- Schreibt `obs.microphoneMuted` und `obs.desktopAudioMuted` in die aktive `overlay-data.json`.
- Erkennt standardmäßig Quellen wie `Mic`, `Mikrofon`, `Broadcast`, `Desktop Audio` und `Spiel- und Streamsound`.
- Vorhandene konfigurierte OBS-Quellennamen werden bevorzugt.
- Das mitgelieferte Metaschutz-Modul verwendet für Broadcast nun `obs.desktopAudioMuted` statt des Stream-Live-Status.
