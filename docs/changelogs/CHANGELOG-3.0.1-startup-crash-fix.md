# Creator Control Suite 3.0.1 – Startup-Crash-Fix

- Verhindert das Speichern der Einstellungen, während die gespeicherten Werte beim Programmstart in die UI geladen werden.
- Raid-Checkboxen und Raid-Zielauswahl reagieren erst nach vollständig geladenem Hauptfenster.
- `JsonSettingsStore.SaveAsync` wiederholt kurzzeitig blockierte Dateiumbenennungen und verwendet anschließend einen direkten Schreib-Fallback.
- Keine Änderung an OBS-Automatisierungen oder Alert-Abläufen.
