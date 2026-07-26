# Creator Control Suite 8.0.0-alpha55

## Spotify-Verlauf – Profilimport mit Konfliktvorschau

- Profilimporte werden vor dem Speichern vollständig geprüft.
- Die Vorschau kennzeichnet Profile als **neu**, **zu aktualisieren** oder **unverändert**.
- Für jedes neue oder geänderte Profil werden die enthaltenen Wiederherstellungsbereiche angezeigt.
- Änderungen werden erst über **IMPORT ÜBERNEHMEN** dauerhaft gespeichert.
- Unveränderte Profile werden übersprungen und nicht unnötig neu geschrieben.
- Formatkennung und Profilversion werden validiert; unbekannte zukünftige Versionen werden sicher abgelehnt.
- Prüf- und Importergebnisse werden in der Automationsdiagnose protokolliert.
