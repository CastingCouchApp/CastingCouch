# Creator Control Suite 2.9.4

## Regieplan – Import, Export und Prüfung

- Komplette Regiepläne können als `.ccs-regieplan.json` exportiert werden.
- Beim Import kann der vorhandene Plan ersetzt oder um die importierten Schritte ergänzt werden.
- Importierte Schritte erhalten neue interne IDs und werden auf sichere Wertebereiche begrenzt.
- Eine neue Regieplanprüfung erkennt fehlende Szenen, leere Namen, doppelte Schrittnamen und unvollständige Twitch-Aktionen.
- Bei bestehender OBS-Verbindung wird geprüft, ob alle verwendeten Szenen tatsächlich vorhanden sind.
- Import, Export und Prüfung werden protokolliert und beenden die Anwendung bei fehlerhaften Dateien nicht.
