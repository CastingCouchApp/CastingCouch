# Creator Control Suite 8.0.0-alpha18

## Persistente Rollout-Aufträge und zentrales Auditprotokoll

- Geplante Multi-PC-Rollouts werden dauerhaft als JSON gespeichert.
- Das ausgewählte Update-Paket wird in einen lokalen, stabilen ScheduledRollouts-Ordner kopiert.
- Nach einem Neustart der Suite wird ein offener Rollout-Auftrag automatisch wiederhergestellt.
- Zielgruppe, Canary-Anzahl, Fehlergrenze, Geräteabstand und Wartungsfenster werden gemeinsam mit dem Auftrag gespeichert.
- Nach erfolgreicher Ausführung oder manueller Aufhebung wird der gespeicherte Auftrag entfernt.
- Sämtliche Multi-PC- und Update-Aktionen werden zusätzlich dauerhaft als JSONL-Auditprotokoll geschrieben.
- Das Auditprotokoll kann direkt im Multi-PC-Bereich geladen werden.

## Speicherorte

- `%LOCALAPPDATA%\\CreatorControlSuite\\multi-pc-scheduled-rollout.json`
- `%LOCALAPPDATA%\\CreatorControlSuite\\ScheduledRollouts`
- `%LOCALAPPDATA%\\CreatorControlSuite\\multi-pc-rollout-audit.jsonl`
