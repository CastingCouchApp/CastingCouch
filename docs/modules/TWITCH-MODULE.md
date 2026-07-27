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
- Chat-`message.fragments` (Text/Emotes/Mentions/Cheermotes) für das Overlay
- Follow
- Sub
- ReSub
- GiftSub
- Cheer
- Raid
- Stream online
- Stream offline

## Emotes für Overlay-Chat

- Twitch-Emotes kommen aus EventSub-Fragments (CDN `static-cdn.jtvnw.net`)
- Badge-Icons über Helix `chat/badges` (global + channel)
- Optional: BTTV / FFZ / 7TV über `Overlay.Chat`-Flags (Katalog-Refresh nach Twitch-Connect)
- Ausgabe als `channel.chat.message` über den Overlay-WebSocket `/ws`
- Optional: EventSub-Events (Follow/Sub/…) im Chat-Overlay (`ShowTwitchEvents`)

## WPF-Oberfläche

- Twitch autorisieren
- verbinden/trennen
- Verbindungsstatus
- Streamtitel
- Kategoriesuche
- Live-Chat empfangen
- Chatnachrichten senden
- letzte Twitch-Events anzeigen
- Chat-Anzeige wählen: eingebauter Chat (EventSub/Helix) oder eingebetteter Twitch-Web-Chat (WebView2)
- Twitch Web-Login mit persistentem WebView2-Profil (`%LocalAppData%\CreatorControlSuite\WebView2\Twitch`)

### Chat-UI-Modus

Unter Einstellungen → Twitch:

- **Eingebauter Chat**: bisherige ListBox-Anzeige, Senden über Helix, Empfang über EventSub
- **Web-Chat (eingebettet)**: Twitch-Popout im WebView2 (Dashboard und Dienste → Twitch)

Der Web-Login ist eine eigene Browser-Session und unabhängig von der Device-Code-API-Anmeldung. API-Tokens können nicht als Web-Cookies übernommen werden.

`EnableChat` steuert weiterhin die EventSub-Chat-Subscription (Stats, Workflow, Dashboard-Chat).

## Nächste Erweiterungen

- EventSub-Wiederverbindung mit lückenloser Übergabe
- Channel Points
- Moderation
- Viewer-/Chatterliste
- Raidzentrale
- Polls und Predictions
- Follower- und Sub-Statistiken
