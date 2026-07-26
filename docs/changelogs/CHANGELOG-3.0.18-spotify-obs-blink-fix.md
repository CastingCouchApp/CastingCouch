# Creator Control Suite 3.0.18

## Spotify-/OBS-Anzeige stabilisiert

- Die Spotify-OBS-Quelle wird nicht mehr bei jedem Spotify- oder JSON-Update neu gesetzt.
- OBS wird nur noch umgeschaltet, wenn „Bei stummgeschalteter Musik ausblenden“ aktiv ist und die Lautstärke tatsächlich zwischen 0 % und über 0 % wechselt.
- Eine beim Start noch unbekannte Spotify-Lautstärke wird nicht mehr als Mute behandelt.
- Die Spotify-Anzeige ist grundsätzlich aktiviert; die frühere Option zum vollständigen Deaktivieren wurde intern fest eingeschaltet und in der Oberfläche ausgeblendet.
- Die Overlay-JSON wird direkt aktualisiert, statt die Datei bei jedem Update durch eine temporäre Datei zu ersetzen. Dadurch werden unnötige Dateiwechsel-Ereignisse vermieden.
