# Transaktionaler Updater

Der Updater sichert betroffene Dateien, schreibt ein Transaktionsjournal,
kopiert das Paket und rollt bei Fehlern in umgekehrter Reihenfolge zurück.
Fehlschläge bleiben zur Diagnose im Transaktionsordner erhalten.

## Write-ahead-Journal

Vor jeder Änderung an einer Installationsdatei wird `PendingFile` atomar in
`transaction.json` gespeichert. Erst danach wird die Datei ersetzt. Nach
erfolgreicher Kopie wird sie nach `AppliedFiles` übernommen und
`PendingFile` gelöscht.

Damit kennt Recovery auch eine Datei, wenn der Prozess oder Rechner exakt
zwischen Dateikopie und anschließendem Journal-Commit ausfällt:

- Vorhandene Dateien werden aus `backup/` wiederhergestellt.
- Neu angelegte Dateien ohne Backup werden entfernt.
- Bereits vollständig angewendete Dateien werden rückwärts zurückgerollt.
- Ein zuvor fehlgeschlagener Rollback kann erneut ausgeführt werden; alte
  Fehlermeldungen werden vor dem neuen Versuch verworfen.

Journal-Updates verwenden eine temporäre Datei und einen atomaren
Move/Replace. `Completed` und `RolledBack` sind terminale Zustände.

Die automatisierte Abbruchmatrix ist in
[`UPDATE-FAILURE-MATRIX.md`](UPDATE-FAILURE-MATRIX.md) dokumentiert.
