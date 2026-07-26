# Creator Control Suite 3.0.0 Alpha 1 – Overlay- und Alert-Ducking-Fix

- Vorhandener Overlay-Ordner wird direkt verwendet; keine Kopie nach LocalAppData.
- `overlay.json` wird als Manifest erkannt.
- Laufzeitdaten werden direkt nach `<Overlay>\data\overlay-data.json` geschrieben.
- OBS-Browserquellen müssen dadurch nicht auf eine Suite-Kopie umgestellt werden.
- Spotify wird während eines Suite-Alerts relativ auf 75 % der vorherigen Lautstärke abgesenkt.
- Nach dem letzten Alert wird die exakt gespeicherte ursprüngliche Lautstärke wiederhergestellt.
- Überlappende Alerts lösen keine mehrfache Absenkung aus.
- Spotify-Overlay wird bei pausierter Wiedergabe ausgeblendet.

Hinweis: Diese Quellversion konnte in der Erstellungsumgebung nicht kompiliert werden, da dort kein .NET SDK installiert ist.
