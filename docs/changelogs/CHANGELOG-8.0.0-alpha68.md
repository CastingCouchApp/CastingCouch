# Creator Control Suite 8.0.0-alpha68

## WPF-Shutdown wird vollständig abgeschlossen

- `App.OnExit` ist nicht länger `async void`.
- WPF erhält erst dann die Kontrolle zurück, wenn `IHost.StopAsync` abgeschlossen, abgebrochen oder fehlgeschlagen ist.
- Das bereits vorhandene Zehn-Sekunden-Zeitlimit bleibt erhalten.
- Logging, Host-Dispose und Single-Instance-Mutex werden weiterhin in der vorgesehenen Reihenfolge abgearbeitet.
- Verhindert, dass Windows den Prozess beendet, während Hintergrunddienste noch Einstellungen, Logs oder Zustände schreiben.

## Version

- Produkt- und Assembly-Version auf `8.0.0-alpha68` aktualisiert.
