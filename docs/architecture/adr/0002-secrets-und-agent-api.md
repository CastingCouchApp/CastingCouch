# ADR 0002: Geschützte Secrets und versionierte Agent-API

- Status: Angenommen
- Datum: 28. Juli 2026

## Entscheidung

Persistierte Zugangsdaten werden ausschließlich über `ISecretStore` mit
Windows-DPAPI gespeichert. Öffentliche Metadaten enthalten keine Secret-Felder.
Die Agent-API wird unter `/api/v1` versioniert; Pairing erfolgt per POST mit
kurzlebigem Code, Fehlversuchslimit, Rate Limit und Audit.

## Kompatibilität

Bestehende `agent-key.txt`, OBS-Passwörter und Geräte-Keys werden beim ersten
Start in den geschützten Store migriert. Klartextquellen werden erst nach
erfolgreichem Speichern bereinigt.
