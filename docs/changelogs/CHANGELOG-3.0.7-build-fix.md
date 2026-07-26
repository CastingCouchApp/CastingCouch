# Creator Control Suite 3.0.7 – Build-Fix

Behoben:
- nicht definierte Variable `playback` beim Aktivieren eines Overlay-Ordners
- ungültiger Enum-Wert `AppLogLevel.Info` durch `AppLogLevel.Information` ersetzt
- ungültiger Logger-Aufruf `_appLogger.Warning(...)` durch `_appLogger.Write(AppLogLevel.Warning, ...)` ersetzt

Es wurden ausschließlich die gemeldeten Compilerfehler korrigiert.
