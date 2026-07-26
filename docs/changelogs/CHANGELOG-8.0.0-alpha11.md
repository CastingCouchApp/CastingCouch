# Creator Control Suite 8.0.0-alpha11

## Remote-Dateien, Agent-Logs und Update-Staging

- Remote-Agent-Logs mit bis zu 2.000 Zeilen abrufbar.
- Overlay-ZIP-Pakete werden TLS-geschützt an den Ziel-PC übertragen und sicher entpackt.
- Vor einer Overlay-Verteilung wird automatisch ein Sicherungsordner angelegt.
- ZIP-Slip-Pfade werden blockiert.
- Suite-Update-ZIPs können auf dem Ziel-PC kontrolliert bereitgestellt und entpackt werden.
- Update-Pakete werden nur gestaged; die laufende Suite wird nicht ungefragt ersetzt.
- Neue Berechtigungen: `files.deploy` und `updates.stage`.
- Agent-Version und LAN-Erkennung auf alpha11 angehoben.
