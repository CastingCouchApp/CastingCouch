# Creator Control Suite 3.0.10

## Spotify
- Der Start einer ausgewählten Playlist prüft den gespeicherten Zugriffstoken jetzt vorab.
- Bei einer Spotify-API-Antwort 401 wird der Zugriffstoken automatisch über den Refresh-Token erneuert und die Aktion einmal wiederholt.
- Ist kein gültiger Refresh-Token vorhanden, wird verständlich zur erneuten Spotify-Autorisierung aufgefordert.

## OBS-Steuerung
- Die Seite Dienste > OBS wurde auf die gewünschte Kernfunktion reduziert.
- Auswahl: Szene, Quelle, Verzögerung in Sekunden und Aktion Einblenden/Ausblenden.
- Mit Hinzufügen wird eine gespeicherte Regel in die darunterliegende Liste aufgenommen.
- Regeln werden nach Aktivierung der gewählten Szene und Ablauf der Verzögerung ausgeführt.
- Ausgewählte Regeln können sofort getestet oder gelöscht werden.
- Übergänge, Filter, Transformationswerte, Ebenen und Audiomixer bleiben ausschließlich in OBS und sind in der Suite ausgeblendet.
