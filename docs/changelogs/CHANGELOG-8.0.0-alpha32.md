# Creator Control Suite 8.0.0-alpha32

## Spotify-Automationsprioritäten

- Jede zeitgesteuerte Spotify-Automation besitzt jetzt eine eigene Priorität von -1000 bis 1000.
- Eine laufende Spotify-Aktion kann nur noch durch eine Regel mit gleicher oder höherer Priorität ersetzt werden.
- Niedriger priorisierte Regeln werden übersprungen, solange eine wichtigere Verzögerung, Playlist-Aktion oder Lautstärkeüberblendung aktiv ist.
- Übersprungene Spotify-Aktionen werden in den Automatisierungsdiagnosen mit Regelname und Prioritäten protokolliert.
- Nach Abschluss oder Abbruch wird die aktive Spotify-Priorität sauber zurückgesetzt.
- Beim Duplizieren einer Regel wird die Spotify-Priorität übernommen.

Beispiel: Die Endszene kann Priorität 100 erhalten. Eine parallel ausgelöste Standardregel mit Priorität 0 kann dann die Endmusik nicht mehr unterbrechen.
