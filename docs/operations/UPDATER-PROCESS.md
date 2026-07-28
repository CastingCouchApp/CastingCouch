# Separater Updater

Der Updater wartet optional auf das Ende der Suite, entpackt das Update-ZIP,
sichert betroffene Dateien, schreibt nach jeder angewendeten Datei ein
Transaktionsjournal, kopiert die Programmdateien und startet die Suite neu.
Bei Fehlern rollt er zurück. Beim nächsten Start werden unvollständige Journale
unter `%LOCALAPPDATA%\CreatorControlSuite\UpdateTransactions` erkannt und vor
einem weiteren Update zurückgerollt.

Aufruf:

```
CreatorControlSuite.Updater.exe <package.zip> <installDir> <mainExe> [waitPid]
```

Signaturprüfung erfolgt in der App vor dem Start. Rechteerhöhung für
Program-Files-Installationen folgt vor der Beta.
