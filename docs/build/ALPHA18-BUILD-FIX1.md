# 2.0.81 – Build Fix 1

Grundlage: realer Windows-Build vom 14.07.2026.

## NU1201

`CreatorControlSuite.LicenseMockServer` zielte auf `net10.0`, referenzierte
aber den Windows-Core `net10.0-windows`. Der Mockserver zielt jetzt ebenfalls
auf `net10.0-windows`.

## CS1061

`SupportPackageService` verwendete die veraltete Eigenschaft `EnableEvents`.
Das aktuelle Settings-Modell verwendet `EnableEventSub`.

## Build-Orchestrierung

Windows PowerShell 5.1 behandelt einen nativen ExitCode ungleich Null nicht
automatisch wie eine PowerShell-Exception. Deshalb lief Alpha 17 trotz
fehlgeschlagenem `dotnet build` weiter.

2.0.81 verwendet `Invoke-NativeChecked` und stoppt unmittelbar beim ersten
fehlerhaften nativen Buildschritt.

## Diagnosepaket

Der frühere Einsatz von `Copy-Item -LiteralPath "...\\*"` expandierte den
Wildcard nicht. 2.0.81 enumeriert Dateien und Ordner mit `Get-ChildItem`
und kopiert die konkreten Pfade.
