# Creator Control Suite 2.9.7

## Zentrale Overlay-Datenquelle

- Die Suite schreibt nur noch eine physische `overlay-data.json`.
- Importierte Overlay-Projekte erhalten in ihrem `data`-Ordner einen Dateiverweis auf die zentrale Datei.
- Bevorzugt wird ein Windows-Hardlink; alternativ wird ein symbolischer Link verwendet.
- Bereits vorhandene kopierte Datendateien werden vor der Umstellung als `legacy-copy-*` gesichert.
- Der Status der Datenverknüpfung wird im Spotify-Overlay-Bereich angezeigt.
- Alte `AdditionalDataRoots` werden nicht mehr beschrieben und beim Speichern bereinigt.
