# Coverage- und Qualitäts-Gates

Stand: 28. Juli 2026

## Ziel

Coverage wird nicht als einzelne globale Prozentzahl behandelt. Die CI nutzt
zwei ergänzende Gates:

1. Die globale Ratsche verhindert, dass Line- oder Branch-Coverage unter die
   gespeicherte Baseline fällt.
2. Sicherheits- und updatekritische Dateien müssen einzeln mindestens 90 %
   Branch-Coverage erreichen.

Die Zielwerte für neue oder geänderte Geschäftslogik bleiben 80 % Coverage,
für sicherheits- und updatekritische Komponenten gelten mindestens 90 %
Branch-Coverage.

## Globale Ratsche

Die Baseline steht in `build/coverage-baseline.json` und wird durch
`build/Test-CoverageBaseline.ps1` geprüft. Sie darf nur zusammen mit einem
begründeten Review geändert werden. Eine Erhöhung ist der Normalfall; eine
Absenkung benötigt eine dokumentierte Risikoakzeptanz.

Aktuelle Baseline:

- Lines: 21,13 %
- Branches: 12,86 %

Die Werte wurden mit dem vollständigen Release-Testlauf am 28. Juli 2026
gemessen. Die zuvor eingetragenen Werte von 36 % beziehungsweise 28 % waren
nicht durch einen Coverage-Bericht belegt und hätten den CI-Job unmittelbar
fehlschlagen lassen.

## Kritische Komponenten

`build/coverage-critical.json` definiert kritische Verzeichnisse und bereits
qualifizierte Dateien. `build/Test-CriticalCoverage.mjs` prüft:

- jede gelistete Datei gegen ihren Branch-Mindestwert;
- dass jede im Pull Request geänderte C#-Datei in einem kritischen Verzeichnis
  explizit in die Policy aufgenommen wurde;
- dass fehlende Dateien oder fehlende Coverage den Build abbrechen.

Damit kann eine neue kritische Komponente nicht ohne eigenen Coverage-Nachweis
in die Codebasis gelangen. Aktuell ist
`FileUpdateTransaction.cs` mit 92 von 92 abgedeckten Branches (100 %) die erste
qualifizierte Komponente.

## Lokale Prüfung

```text
node --test build/tests/Test-CriticalCoverage.test.mjs
node build/Test-CriticalCoverage.mjs <coverage.cobertura.xml> build/coverage-critical.json
```

Der vollständige Coverage-Bericht wird auf Windows durch den .NET-CI-Job
erzeugt und anschließend von beiden Gates geprüft.
