# CastingCouch Tauri — Sidecar-Vertrag

Übergangsbrücke für Module, die noch in .NET leben (YouTube Music, Workflow, Agent).

## Start

Tauri spawnt optional:

```
CreatorControlSuite.CommandClient.exe --sidecar --port 18765
```

oder den bestehenden Agent unter HTTPS.

## API (Loopback JSON)

| Methode | Pfad | Zweck |
|---------|------|--------|
| GET | `/sidecar/health` | liveness |
| POST | `/sidecar/workflow/run` | Run-of-Show Schritt |
| GET | `/sidecar/ytm/now-playing` | YouTube Music Bridge |

Kein Named-Pipe mehr auf macOS. Windows kann übergangsweise beides anbieten.

Sobald `ccs-modules` das Feature hat, wird der Sidecar-Pfad entfernt.
