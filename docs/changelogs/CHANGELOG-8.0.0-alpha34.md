# Creator Control Suite 8.0.0-alpha34

## Spotify-Automationen: Zustand sichern und wiederherstellen

- Neue Spotify-Aktion `Vorherige Wiedergabe wiederherstellen`.
- Automatisierungsregeln können den aktuellen Spotify-Zustand gruppenbezogen sichern.
- Gesichert werden Kontext bzw. Titel, Wiedergabeposition, Lautstärke, Shuffle, Wiederholungsmodus und Pause-/Play-Status.
- Jede Spotify-Automationsgruppe besitzt einen eigenen gespeicherten Zustand.
- Die Wiederherstellung kann mit dem bestehenden Fade-Wert weich eingeblendet werden.
- Nach erfolgreicher Wiederherstellung wird der gespeicherte Zustand der Gruppe entfernt.
- Fehlende gespeicherte Zustände erzeugen eine verständliche Diagnose statt eines unklaren Fehlers.

## Beispiel

Die Regel für die Pausenszene sichert die laufende Game-Musik und startet eine Pausen-Playlist. Beim Zurückwechseln verwendet eine zweite Regel derselben Gruppe die Aktion `Vorherige Wiedergabe wiederherstellen`. Danach läuft die ursprüngliche Musik an der vorherigen Position und Lautstärke weiter.
