# Creator Control Suite 8.0.0-alpha102 Hotfix 11

- Ursache für wechselnde Spotify- und Live-Zustände behoben.
- Erkennt die alte `StreamingSuite/Start.ps1` im DenverJohn-Overlay.
- Beendet nur den laufenden PowerShell-Prozess, dessen Befehlszeile genau dieses Legacy-Skript enthält.
- Benennt `Start.bat`, `Start.vbs` und `Start.ps1` in Sicherungsdateien mit der Endung `.disabled-by-creator-control-suite` um.
- Verhindert damit, dass die alte StreamingSuite und die Creator Control Suite gleichzeitig `Overlay/data/overlay-data.json` überschreiben.
- Legt `StreamingSuite/LEGACY-WRITER-DISABLED.txt` als nachvollziehbaren Hinweis an.
