# Creator Control Suite 8.0.0-alpha102 Hotfix 4

## Behoben

- Startabsturz beim parallelen Speichern von `settings.json` behoben.
- `JsonSettingsStore.SaveAsync` serialisiert Schreibzugriffe jetzt mit `SemaphoreSlim`.
- Jeder Speichervorgang verwendet eine eigene temporäre Datei statt der gemeinsam genutzten `settings.json.tmp`.
- Temporäre Dateien werden auch bei Abbruch oder Fehlern sicher bereinigt.
- Der bestehende atomare Austausch, die Sicherung als `settings.json.bak` und der direkte Fallback bleiben erhalten.

## Ursache

Mehrere während des Fensterstarts ausgelöste Einstellungsänderungen konnten gleichzeitig dieselbe temporäre Datei verwenden. Nachdem ein Vorgang diese Datei verschoben hatte, löste der nächste Vorgang eine `FileNotFoundException` aus.
