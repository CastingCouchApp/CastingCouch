# Creator Control Suite 8.0.0-alpha14

## Remote-Update-Manifeste und Historie

- Update-Pakete erhalten beim Bereitstellen ein HMAC-SHA256-signiertes Agent-Manifest.
- Die Validierung prüft Paketinhalt, Manifest-Signatur und Agent-Kompatibilität.
- Paketversion und erforderliche Mindest-Agent-Version werden im Remote-Status angezeigt.
- Neuer Endpunkt `GET /api/update/history` liefert bis zu 100 Update-Ereignisse.
- Bereitstellung, Prüfung, Anwendung und Rollback werden dauerhaft in `update-history.json` protokolliert.
- Neue Multi-PC-Schaltfläche `UPDATE-HISTORIE` mit eigener Verlaufsliste.
- Agent- und Discovery-Version auf `8.0.0-alpha14` angehoben.

## Sicherheit

Das Manifest wird mit dem individuellen Agent-Schlüssel signiert. Manipulierte Status- oder Manifestdaten werden bei der Paketprüfung abgelehnt.
