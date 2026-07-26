# Creator Control Suite 8.0.0-alpha54

## Spotify-Wiederherstellungsprofile: Import und Export

- Eigene Wiederherstellungsprofile können als formatierte JSON-Datei exportiert werden.
- Exportdateien enthalten Formatkennung, Versionsnummer, Exportzeitpunkt und die Profile.
- Profile können aus Alpha54-Exportdateien sowie aus einfachen JSON-Prof arrays importiert werden.
- Gleichnamige eigene Profile werden beim Import aktualisiert; neue Profile werden ergänzt.
- Integrierte Standardprofile werden niemals überschrieben oder importiert.
- Leere, ungültige und beschädigte Dateien werden abgefangen und verständlich gemeldet.
- Import und Export werden in der Automationsdiagnose protokolliert.
