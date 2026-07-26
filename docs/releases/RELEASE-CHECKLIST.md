# Release-Checkliste

## Build
- [ ] Release-Build und Tests erfolgreich
- [ ] Haupt-EXE, CommandClient und Updater vorhanden
- [ ] MSI mit Version aus Directory.Build.props
- [ ] `update-manifest.json` signiert erzeugt
- [ ] Installer-Binlog geprüft

## Sicherheit
- [ ] keine Private Keys im Projekt/Installer
- [ ] `update-public.pem` vorhanden
- [ ] GitHub Secret `UPDATE_SIGNING_KEY_PEM` gesetzt
- [ ] Update-Manifest und Paket geprüft

## Recht
- [ ] EULA juristisch geprüft
- [ ] Datenschutzhinweise juristisch geprüft
- [ ] Spotify-Nutzungsmodell geprüft

## End-to-End
- [ ] OBS/Twitch/Spotify verbunden
- [ ] Chat und Events getestet
- [ ] Alerts und Workflow komplett getestet
- [ ] Stream Deck, Backup/Restore und Update/Rollback getestet
- [ ] In-App-Update gegen GitHub Release getestet

## Release
- [ ] GitHub Release enthält ZIP, MSI und `update-manifest.json`
- [ ] Installation, Upgrade und Deinstallation auf sauberem Windows getestet
