# Creator Control Suite 4.2.0

## Automation Dependencies & Retry

- Regeln können von einer zuvor erfolgreich ausgeführten Regel abhängig gemacht werden.
- Fehlgeschlagene Aktionen können automatisch bis zu 20-mal wiederholt werden.
- Zwischen Wiederholungen ist eine Pause von 0 bis 3600 Sekunden einstellbar.
- Nach endgültigem Fehler kann automatisch eine Ersatzregel gestartet werden.
- Diagnosemeldungen zeigen Versuche, Wartezeiten, endgültige Fehler und Ersatzregeln.
- Import/Export erhält Abhängigkeiten und Ersatzregel-Verknüpfungen mit neuen IDs.
- Validierung erkennt fehlende und direkte selbstreferenzierende Abhängigkeiten.
