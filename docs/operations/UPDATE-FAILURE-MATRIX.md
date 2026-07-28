# Update-Abbruch- und Recovery-Matrix

Stand: 28. Juli 2026

## Automatisierter Nachweis

| Szenario | Erwartetes Ergebnis | Test |
|---|---|---|
| Fehler nach teilweise angewendeten Bestands- und Neudateien | Bestandsdateien wiederhergestellt, Neudateien entfernt | `ApplyAsync_RollsBackChangedAndNewFiles_AfterPartialFailure` |
| Cancellation zwischen mehreren Dateien | Vollständiger Rollback, Fehlerursache bleibt `OperationCanceledException` | `ApplyAsync_RollsBack_WhenCancellationInterruptsMultipleFiles` |
| Prozessabbruch nach vollständig protokollierten Dateien | Recovery stellt Backups wieder her und entfernt Neudateien | `RecoverAsync_RollsBackJournalLeftByProcessInterruption` |
| Prozessabbruch zwischen Kopie und Journal-Commit bei Bestandsdatei | Write-ahead-Eintrag stellt Backup wieder her | `RecoverAsync_RollsBackWriteAheadPendingFile_AfterInterruption` |
| Prozessabbruch zwischen Kopie und Journal-Commit bei Neudatei | Write-ahead-Eintrag entfernt die Neudatei | `RecoverAsync_RemovesWriteAheadPendingNewFile_AfterInterruption` |
| Vorheriger Rollback scheitert an temporärer Sperre | Späterer Retry leert alte Fehler und endet in `RolledBack` | `RecoverAsync_ClearsPreviousErrors_WhenRollbackRetrySucceeds` |
| Erfolgreiche Installation | Dateien und Journal enden in `Completed` | `ApplyAsync_LeavesCompletedInstallAndJournal` |
| ZIP-Pfadüberschreitung | Extraktion wird vor Apply abgelehnt | `SafeZipExtractorTests` |
| Falsche Manifest-Signatur oder SHA-256 | Paket wird vor Extraktion abgelehnt | `UpdateManifestSignatureTests` |

## Noch erforderlicher Windows-E2E-Nachweis

Vor Verkaufsfreigabe muss die Matrix zusätzlich auf `windows-latest` und
einer sauberen Windows-VM ausgeführt werden:

1. Prozess während Backup, Apply und Rollback hart beenden.
2. Rechner während Apply neu starten.
3. Eine Zieldatei exklusiv sperren und den Retry nach Freigabe prüfen.
4. Update auf sauberer sowie bestehender MSI-Installation anwenden.
5. Startfähigkeit, Upgrade, Rollback und Uninstall protokollieren.

Die automatisierten Tests belegen die plattformneutrale
Transaktionssemantik. Sie ersetzen nicht den offenen Windows-/MSI-E2E-Gate.
