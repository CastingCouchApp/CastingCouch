# Creator Control Suite 8.0.0-alpha31

## Spotify-Automationen: Abbruch- und Rücksetzlogik

- Jede neue Spotify-Automation beendet eine noch laufende Spotify-Verzögerung oder Lautstärkeüberblendung.
- Schnelle OBS-Szenenwechsel lösen dadurch keine verspäteten Playlist-Starts mehr aus.
- Überlappende Lautstärke-Fades werden verhindert.
- Der zuletzt ausgelöste Spotify-Befehl gewinnt zuverlässig.
- Abbruchsignale der übergeordneten Automatisierungsregel werden weiterhin berücksichtigt.
- Temporäre CancellationTokenSource-Instanzen werden nach Abschluss sauber freigegeben.

## Beispiel

Wird von „Start“ direkt zu „Game“ gewechselt, bevor der verzögerte Playlist-Start der Startszene ausgeführt wurde, wird dieser verworfen. Es wird nur noch die Spotify-Aktion der Gameszene ausgeführt.
