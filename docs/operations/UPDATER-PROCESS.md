# Separater Updater

Der Updater wartet optional auf das Ende der Suite, entpackt das Update-ZIP,
sichert betroffene Dateien, schreibt ein Transaktionsjournal, kopiert die
Programmdateien und startet die Suite neu. Bei Fehlern rollt er zurück.

Aufruf:

```
CreatorControlSuite.Updater.exe <package.zip> <installDir> <mainExe> [waitPid]
```

Signaturprüfung erfolgt in der App vor dem Start. Rechteerhöhung für
Program-Files-Installationen folgt vor der Beta.
