# Creator Control Suite 8.0.0-alpha10

## Remote-OBS-Presets und Sicherheitszustände

- Aktuellen OBS-Zustand eines gekoppelten Streaming-PCs als benanntes Preset sichern.
- Presets werden dauerhaft auf dem Remote-Agenten in `%LOCALAPPDATA%\CreatorControlSuite\Agent\obs-presets.json` gespeichert.
- Gesichert werden aktives OBS-Profil, Szenensammlung, Programmszene, Audiopegel, Mute-Zustände und Sichtbarkeit der Quellen der aktuellen Szene.
- Vorhandene Presets können geladen, wiederhergestellt und gelöscht werden.
- Gleichnamige Presets werden kontrolliert ersetzt.
- Die Preset-Datei wird über eine temporäre Datei atomar geschrieben.
- Alle Preset-Routen erfordern TLS-Vertrauen, Agent-Schlüssel und `obs.control`.

## Agent-Endpunkte

- `GET /api/obs/presets`
- `POST /api/obs/presets/save`
- `POST /api/obs/presets/apply`
- `POST /api/obs/presets/delete`
