# Creator Control Suite 8.0.0-alpha67

## Shutdown-Stabilisierung

- Globale Fehlerhandler werden vor dem kontrollierten Beenden wieder abgemeldet.
- `IHost.StopAsync` erhält ein Zeitlimit von 10 Sekunden.
- Timeout- und Shutdown-Fehler werden protokolliert, ohne einen zweiten Absturz auszulösen.
- `IHost.Dispose` ist separat abgesichert.
- Das Freigeben des Single-Instance-Mutex ist gegen `ApplicationException` geschützt.
- Host- und Mutex-Referenzen werden nach dem Beenden zurückgesetzt.
- Projektversion auf `8.0.0-alpha67` aktualisiert.
