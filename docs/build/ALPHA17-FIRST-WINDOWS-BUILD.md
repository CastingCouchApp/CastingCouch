# 2.0.81 – Erster realer Windows-Build

2.0.81 fügt bewusst keine neuen Streaming-Funktionen hinzu.

## Start

ZIP entpacken und anschließend ausführen:

`build\Run-FirstWindowsBuild.cmd`

Das Skript führt nacheinander aus:

1. Build-Umgebung diagnostizieren
2. Preflight
3. NuGet Restore
4. Solution Build
5. Tests
6. App Publish
7. Publish-Layout-Prüfung
8. Installer-Build

## Bei einem Fehler

Automatisch werden Diagnoseinformationen unter `artifacts` gesammelt.

Danach ausführen:

`build\Collect-BuildFailure.ps1`

Das erzeugte ZIP enthält:

- Build-Transcript
- MSBuild-Binlogs
- Restore-/Test-/Publish-Diagnose
- Umgebungsbericht
- Solution und zentrale Build-Konfiguration

Es enthält absichtlich keine OAuth-Secrets aus LocalAppData.

## Ziel

Ab 2.0.81 werden keine Compiler- oder WiX-Fehler mehr theoretisch erraten.
Der echte Windows-Build ist die Fehlerquelle und wird konkret korrigiert.
