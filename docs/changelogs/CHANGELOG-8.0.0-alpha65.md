# Creator Control Suite 8.0.0-alpha65

## Behoben

- Die Installations- und Upgrade-Erkennung verwendet nicht mehr die veraltete feste Version `3.1.1`.
- Die aktuell laufende Produktversion wird nun automatisch aus `AssemblyInformationalVersion` gelesen.
- Optionale Build-Metadaten hinter `+` werden für Versionsvergleiche entfernt.
- Als Rückfallebene wird die Assembly-Version verwendet.

Dadurch werden Erstinstallation, normales Starten und Upgrades wieder mit der tatsächlichen Suite-Version protokolliert.
