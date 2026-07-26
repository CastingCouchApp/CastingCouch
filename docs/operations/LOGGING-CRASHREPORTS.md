# Logging und Crashberichte – 2.0.81

## Logs

Ablage:

`%LOCALAPPDATA%\CreatorControlSuite\Logs`

Format:

- JSON Lines
- eine Meldung pro Zeile
- Zeitstempel
- Level
- Kategorie
- Nachricht
- Exception
- zusätzliche Eigenschaften

Die Logansicht kann:

- pausiert werden
- durchsucht werden
- nach Level gefiltert werden
- einzelne Meldungen kopieren
- Meldungen als Textdatei exportieren

## Crashberichte

Ablage:

`%LOCALAPPDATA%\CreatorControlSuite\CrashReports`

Erfasst werden:

- Programmversion
- Windows-Version
- .NET-Version
- Architektur
- Exception-Typ
- Meldung
- Stacktrace
- vollständige Exception
- Fehlerquelle

Crashberichte werden nicht automatisch hochgeladen.
