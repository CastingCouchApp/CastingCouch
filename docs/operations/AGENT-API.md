# Agent-API v1

Stand: 28. Juli 2026

Der lokale Agent stellt ausschließlich versionierte HTTPS-Routen unter
`/api/v1` bereit. Legacy-Aufrufe unter `/api` werden vorübergehend auf v1
umgeschrieben und mit einem `Deprecation`-Header markiert.

## Endpunktgruppen

| Gruppe | Routen | Schutz |
|---|---:|---|
| Operations | Status, Befehle, Logs, Einstellungen, Historie | Agent-Credential; Befehle zusätzlich über den konkreten Command |
| Security | Pairing, Rotation, Entkopplung | Pairing-Code beziehungsweise bestehendes Agent-Credential |
| OBS | 19 Status- und Steuerungsrouten | Agent-Credential und `obs.control` |
| Updates | Stage, Status, Historie, Validate, Apply, Rollback | Agent-Credential; Mutation zusätzlich über `updates.stage` oder `updates.apply` |

Das Agent-Credential wird im Header `X-CCS-Agent-Key` übertragen. Pairing
erfolgt ausschließlich per `POST /api/v1/pair`; der kurzlebige Code steht im
JSON-Body. Der Pairing-Endpunkt besitzt zusätzlich Rate Limiting, maximales
Payload-Limit und ein Versuchslimit.

## Fehlervertrag und Größenlimits

Fehler werden einheitlich als `application/problem+json` nach RFC 7807
ausgegeben. Das zusätzliche Feld `code` ist der stabile, maschinenlesbare
Fehlercode, zum Beispiel `agent.authentication_required`,
`agent.permission_required`, `agent.invalid_request`,
`agent.payload_too_large` oder `agent.rate_limited`. Interne Exceptions und
eingesendete JSON-Inhalte werden nicht an Clients oder die Befehlshistorie
zurückgegeben.

| Anfrage | Maximale Body-Größe |
|---|---:|
| `POST /api/v1/pair` | 4 KiB |
| `POST /api/v1/update/stage` | 140 MiB |
| übrige Agent-Anfragen | 1 MiB |

Übergroße Anfragen werden vor dem Endpunkt mit HTTP 413 abgewiesen.
Fehlerhaftes JSON liefert HTTP 400, fehlende Authentifizierung HTTP 401,
fehlende Rechte HTTP 403 und Rate-Limit-Verletzungen HTTP 429.

## Architektur

`Program.cs` erzeugt Stores, Schlüsselmaterial, Zertifikat, Laufzeitzustand und
Adapter und komponiert vier Endpunktgruppen:

- `OperationsEndpointMappings`
- `SecurityEndpointMappings`
- `ObsEndpointMappings`
- `UpdateEndpointMappings`

Die Mapping-Gruppen erhalten Abhängigkeiten explizit und öffnen in Vertrags-
oder Authentifizierungstests keine Prozesse, OBS-Verbindungen oder Updates.
Architekturtests verbieten direkte `MapGet`-/`MapPost`-Implementierungen im
Composition Root sowie lokale, vom zentralen Problem-Factory abweichende
Fehlerantworten in den Endpunktgruppen.

## Noch offen

- Windows-E2E für Pairing, Rotation, Entkopplung und Update-Recovery.
