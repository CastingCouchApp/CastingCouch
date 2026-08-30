# CastingCouch Tauri — Sidecar-Vertrag

Übergangsbrücke für Module, die noch in .NET leben: **YouTube Music**, **Workflow** (Run-of-Show-Schritt) und **Agent**. OBS, Twitch, Spotify, Alerts und Overlay laufen in Rust und brauchen den Sidecar nicht.

Sobald `ccs-modules` ein Feature nativ hat, fällt der Sidecar-Pfad für dieses Feature weg.

## Wann starten

Der Tauri-Host spawnt den Sidecar **nur wenn alle Bedingungen gelten**:

1. Windows (unter **macOS kein Spawn**, kein Named Pipe).
2. Flag: `settings.json` → `Sidecar.Enabled: true` **oder** Umgebungsvariable `CCS_SIDECAR=1`.
3. Binary vorhanden: `Sidecar.BinaryPath` (Datei existiert) oder `CreatorControlSuite.CommandClient.exe` **neben der Tauri-EXE**.

Ohne Flag oder ohne Binary bleibt der Status `disconnected` — die App crasht nicht.

```json
"Sidecar": {
  "Enabled": false,
  "Port": 18765,
  "BinaryPath": ""
}
```

## Start (Windows)

```
CreatorControlSuite.CommandClient.exe --sidecar --port 18765
```

Nur Loopback JSON. Der Agent unter HTTPS ist ein anderes Thema und nicht Teil dieses Vertrags.

## Ports

| Dienst | Default | Bind |
|--------|---------|------|
| Overlay | `127.0.0.1:8765` | Rust Overlay-Server |
| Sidecar | `127.0.0.1:18765` | .NET CommandClient |

- Overlay-Port belegt (z. B. laufende WPF-Instanz) → Sidecar startet nicht, Status `error`: `Overlay-Port 8765 ist belegt`.
- Sidecar-Port belegt → kein Spawn, Status `error`: `Sidecar-Port 18765 ist belegt`.
- WPF und Tauri nicht parallel auf denselben Ports betreiben.

## API (Loopback JSON, camelCase)

| Methode | Pfad | Zweck |
|---------|------|--------|
| GET | `/sidecar/health` | liveness |
| POST | `/sidecar/workflow/run` | Run-of-Show Schritt (Stub, kein volles RoS) |
| GET | `/sidecar/ytm/now-playing` | YouTube Music Bridge |

### Beispiele

`GET /sidecar/health`

```json
{ "ok": true }
```

`GET /sidecar/ytm/now-playing`

```json
{
  "provider": "ytmusic",
  "connected": false,
  "isPlaying": false,
  "title": "",
  "artist": "",
  "album": "",
  "statusText": "Nicht verbunden"
}
```

`POST /sidecar/workflow/run` mit `{ "command": "workflow.prepare" }`

```json
{ "ok": false, "message": "Run-of-Show noch nicht im Sidecar" }
```

Tauri-Commands: `sidecar_status`, `sidecar_ytm_now_playing` (HTTP-Passthrough), `sidecar_workflow_run` (`{ "command": "workflow.prepare" }`, Default `workflow.prepare`). Ohne Sidecar: `sidecar_workflow_run` liefert `{ "ok": false, "message": "Sidecar nicht verbunden" }` statt HTTP.
