# Build-Fehler-Workflow

## 1. Build starten

`build\Run-FirstWindowsBuild.cmd`

## 2. Fehler nicht manuell „wegprobieren“

Keine Projektdateien oder Package-Versionen auf Verdacht ändern.

## 3. Diagnosepaket verwenden

Bei einem Fehler wird automatisch versucht, ein Build-Fehlerpaket zu erzeugen.

Alternativ:

```powershell
.\build\Collect-BuildFailure.ps1
```

## 4. Relevante Dateien

Besonders wichtig sind:

- `artifacts\triage\alpha17-build-*.txt`
- `artifacts\triage\LAST-BUILD-FAILURE.txt`
- `artifacts\build-logs\alpha17-build.binlog`
- `artifacts\build-logs\alpha17-tests.binlog`
- `artifacts\build-logs\publish.binlog`
- `artifacts\build-logs\installer.binlog`

## 5. Fehlerreihenfolge

Fehler werden in dieser Reihenfolge behoben:

1. SDK-/Toolchain
2. Restore
3. Compiler
4. Tests
5. Publish
6. Publish-Layout
7. WiX/Installer
8. Installation
9. Programmstart
10. Laufzeitmodule

Dadurch werden keine Folgefehler vor ihrer eigentlichen Ursache bearbeitet.
