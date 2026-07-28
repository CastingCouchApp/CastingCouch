# CastingCouch 2.0.116 – vollständiger Teststand

Dieses Paket enthält den vollständigen rekonstruierten Quellstand bis Version 2.0.116.

## Portable Windows-Version erzeugen

Auf einem Windows-Rechner mit passendem .NET SDK:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\build\Publish-Portable-Win64.ps1
```

Die portable Ausgabe liegt anschließend unter:

`artifacts\portable\CreatorControlSuite-2.0.116-win-x64`

Der erzeugte Ordner kann vollständig auf einen anderen Windows-Rechner kopiert werden. Durch `--self-contained true` ist auf dem Zielrechner keine separate .NET Runtime erforderlich.

## Rekonstruierte Änderungen

- 2.0.114: OBS-Szenenquellen anzeigen und ein-/ausblenden.
- 2.0.115: Schutz vor veralteten asynchronen Quellenabfragen, Auswahl-Erhalt und manuelle Aktualisierung.
- 2.0.116: stabilisierte OBS-Audiomixer-Steuerung mit Live-Status, Eingabevalidierung, Auswahl-Erhalt und Fehlerfeedback.
