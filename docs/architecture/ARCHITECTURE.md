# Architektur

## Anwendung

- WPF/.NET 10
- Dependency Injection
- JSON-Konfiguration
- DPAPI-Secrets
- getrennte Module
- Diagnose-Service
- Profile
- Updates und Backups

## Installation

Der Installer installiert nur Programmdateien und Verknüpfungen.

Nach der Installation werden in der Anwendung eingerichtet:

- OBS-WebSocket und Passwort
- Twitch Client-ID, Client-Secret und OAuth
- Spotify Client-ID, Client-Secret und OAuth
- Alert-Medien und Audiokanal
- Overlay-Ordner
- Szenen und Workflow
- Stream-Deck-Plugin

Dadurch bleiben Updates und Neuinstallationen sauber von Benutzerkonten
und Tokens getrennt.
