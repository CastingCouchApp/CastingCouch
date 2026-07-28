# Update und Rollback

## In-App

- Kanäle: Stable, Beta, Alpha (GitHub Releases dieses Repos)
- Automatische Prüfung beim Start (optional)
- Manuelle Suche und Installation unter Einstellungen → Updates
- Download mit Fortschritt, SHA-256- und Signaturprüfung
- Optional Backup der Nutzerdaten vor dem Apply
- Apply über `CreatorControlSuite.Updater.exe` (transaktional)
- Write-ahead-Journal für Prozess-/Stromabbruch zwischen Kopie und
  Journal-Commit
- Automatische Recovery unvollständiger Transaktionen beim nächsten
  Updater-Start
- Wiederholbarer Rollback nach temporären Dateisperren

Die vollständige Nachweismatrix steht unter
[`UPDATE-FAILURE-MATRIX.md`](UPDATE-FAILURE-MATRIX.md).

## Backups

Enthalten `settings.json`, Profile, Overlay und Secrets unter dem lokalen Datenordner.

## Installer vs. Update

- Erstinstallation / Upgrade-Pfad: MSI
- Laufende Installation: signiertes ZIP + Updater
