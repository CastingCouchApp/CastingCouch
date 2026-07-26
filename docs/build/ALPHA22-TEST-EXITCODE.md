# 2.0.81 – Test-ExitCode

Alpha 21 führte alle 29 Tests erfolgreich aus, behandelte den Lauf danach
jedoch als Fehler, weil `Start-Process` keinen zuverlässig auswertbaren
ExitCode lieferte.

2.0.81 verwendet direkt `System.Diagnostics.Process`.

Wichtige Reihenfolge:

1. Prozess starten
2. stdout/stderr asynchron lesen
3. mit Timeout auf Prozessende warten
4. nochmals `WaitForExit()` aufrufen
5. ExitCode lesen
6. TRX-Datei als zweite unabhängige Bestätigung auswerten

Damit kann ein bestandener Testlauf nicht mehr wegen eines leeren
PowerShell-ExitCodes fälschlich abgebrochen werden.
