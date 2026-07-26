# Creator Control Suite 8.0.0-alpha102 Hotfix 5

## Build-Fix

- CS8604 in `JsonSettingsStore.SaveAsync` behoben.
- Der eindeutige, nicht-nullbare Pfad des aktuellen Speichervorgangs wird nun getrennt vom optionalen Cleanup-Pfad geführt.
- `File.Move` und `File.OpenRead` erhalten dadurch garantiert einen gültigen `string`.
- Der Race-Condition-Fix aus Hotfix 4 bleibt unverändert aktiv.
