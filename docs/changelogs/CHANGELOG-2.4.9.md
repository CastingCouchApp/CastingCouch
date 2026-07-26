# Creator Control Suite 2.5.0

## Spotify API-Diagnose

- Protokolliert jeden Spotify-Web-API-Aufruf mit Uhrzeit, Methode, Endpunkt, laufender Anfragenummer, HTTP-Status, Laufzeit und Retry-After.
- Protokolliert auch OAuth-Token- und Token-Refresh-Aufrufe.
- Zugriffstoken, Refresh-Token, Autorisierungscode und Request-Inhalte werden nicht protokolliert.
- HTTP 429 wird in API und OAuth einheitlich als SpotifyRateLimitException behandelt.
- Die Einträge erscheinen im Bereich Protokolle unter den Kategorien Spotify.Api und Spotify.OAuth.
