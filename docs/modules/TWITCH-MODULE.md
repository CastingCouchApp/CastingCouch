# Twitch-Modul – 2.0.81

## Authentifizierung

Die Desktop-Anwendung verwendet den Twitch Device Code Grant.

Vorteile:

- kein Client-Secret in der installierten Anwendung erforderlich
- Twitch-Autorisierung erfolgt nach der Installation
- Access- und Refresh-Token werden per Windows DPAPI gespeichert
- vorhandene Tokens werden validiert
- ablaufende Tokens werden automatisch erneuert
- Rotations-Refresh-Tokens werden nach jedem Refresh ersetzt

## Twitch API

Implementiert:

- aktuellen Benutzer laden
- Kanal über Login suchen
- Kanalinformationen laden
- Streamtitel ändern
- Kategorie suchen
- Kategorie ändern
- Chatnachricht über Helix senden
- EventSub-Subscriptions erstellen

## EventSub WebSocket

Implementiert:

- session_welcome
- WebSocket Session-ID
- WebSocket-Subscriptions
- keepalive-kompatible Empfangsschleife
- session_reconnect
- revocation
- Chatnachrichten
- Follow
- Sub
- ReSub
- GiftSub
- Cheer
- Raid
- Stream online
- Stream offline

## WPF-Oberfläche

- Twitch autorisieren
- verbinden/trennen
- Verbindungsstatus
- Streamtitel
- Kategoriesuche
- Live-Chat empfangen
- Chatnachrichten senden
- letzte Twitch-Events anzeigen

## Nächste Erweiterungen

- EventSub-Wiederverbindung mit lückenloser Übergabe
- Channel Points
- Moderation
- Viewer-/Chatterliste
- Raidzentrale
- Polls und Predictions
- Follower- und Sub-Statistiken
