# Creator Control Suite 8.0.0-alpha33

## Spotify-Automationsgruppen und gegenseitige Sperre

- Jede Spotify-Automatisierungsregel kann einer frei benennbaren Gruppe zugeordnet werden.
- Regeln derselben Gruppe ersetzen eine bereits laufende Spotify-Aktion kontrolliert.
- Eine Regel kann andere Spotify-Gruppen während ihrer Ausführung exklusiv sperren.
- Eine gesperrte Gruppe kann nur mit höherer Priorität übernehmen.
- Bei gleicher Priorität bleibt die bereits aktive exklusive Gruppe geschützt.
- Abgewiesene Gruppenwechsel erscheinen mit Gruppenname und Ursache in der Automatisierungsdiagnose.
- Aktive Gruppen- und Sperrinformationen werden nach Abschluss oder Abbruch vollständig zurückgesetzt.
- Gruppenname und Exklusivoption werden dauerhaft in den Regeln gespeichert und beim Duplizieren über die vorhandene Regelkopie übernommen.

## Prüfung

- MainWindow.xaml erfolgreich als XML geparst.
- Klammerstruktur der geänderten C#-Dateien geprüft.
- Vollständiger .NET-Build in der Arbeitsumgebung nicht möglich, da kein passendes .NET-SDK verfügbar ist.
