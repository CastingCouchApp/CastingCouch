# Creator Control Suite 8.0.0-alpha72

## IPC parallel client handling

- Named-Pipe-Verbindungen werden nicht mehr seriell in der Annahmeschleife verarbeitet.
- Langsame oder fehlerhafte Clients blockieren dadurch keine weiteren IPC-Befehle mehr.
- Aktive Client-Aufgaben werden zentral verfolgt und beim Shutdown berücksichtigt.
- Jede Verbindung behält ihr eigenes Anfragezeitlimit von fünf Sekunden.
- Verbindungsressourcen werden nach Abschluss oder Abbruch zuverlässig freigegeben.
- Fehler einzelner Client-Aufgaben werden protokolliert, ohne den Server zu beenden.
