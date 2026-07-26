# Creator Control Suite 8.0.0-alpha13

## Remote-Update-Sicherheit

- SHA-256-Prüfsumme und Dateianzahl beim Staging
- explizite Paketvalidierung vor der Installation
- Wartungsmodus während des Dateiaustauschs
- Health-Check des Suite-Prozesses nach dem Neustart
- optionales automatisches Rollback bei fehlgeschlagenem Health-Check
- Update-Ergebnis wird beim nächsten Agent-Start in den Status übernommen
- neue UI-Schaltfläche „Paket prüfen“ und Rollback-Option

## Agent API

- `POST /api/update/validate`
- erweiterte Antworten von `GET /api/update/status`

## Hinweis

Ein vollständiger Build muss auf einem Windows-System mit .NET 10 SDK geprüft werden.
