# 2.0.81 – finaler Test-ExitCode-Fix

Die vorherigen Varianten verwendeten `Start-Process`. Unter Windows
PowerShell 5.1 war der zurückgelesene `ExitCode` in diesem Ablauf unzuverlässig.

2.0.81 führt `dotnet test` direkt als nativen Befehl aus und speichert
`$LASTEXITCODE` unmittelbar nach dem Prozessende.

Zusätzlich bleibt die TRX-Prüfung erhalten. Der Build akzeptiert den
Testschritt nur, wenn:

- der native ExitCode 0 ist
- die TRX-Datei vorhanden ist
- Outcome abgeschlossen ist
- Failed = 0
- Passed = Total
