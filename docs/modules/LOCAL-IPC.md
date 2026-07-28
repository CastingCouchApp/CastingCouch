# Lokale IPC

Die Suite stellt die Named Pipe `CreatorControlSuite.CommandPipe.v1` bereit.
Sie verwendet `PipeOptions.CurrentUserOnly` und öffnet keinen Netzwerk-Port.
Pro Verbindung wird genau ein UTF-8-JSON-Befehl als einzelne Zeile gelesen und
eine `IpcResponse` als einzelne JSON-Zeile geschrieben. Eine Anfrage läuft nach
fünf Sekunden ab, ohne die Annahme weiterer Clients zu blockieren.

Unterstützte Befehlsgruppen:

- `system.*` für Ping und Status
- `workflow.*` für Prepare, Countdown, Live, Pause, Resume und Ende
- `alert.*` für interne und externe Alert-Aktivität
- `obs.*` für Szene und Mute
- `spotify.*` für Wiedergabe, Lautstärke und Playlists
- `stream.*` für Start und Stop

`CreatorControlSuite.CommandClient.exe` kann von Stream Deck oder lokalen
Automationen aufgerufen werden.

`NamedPipeIpcContractTests` prüft den echten Client/Server-Roundtrip,
Fehlerantworten auf ungültiges JSON, die Nutzbarkeit nach einer fehlerhaften
Anfrage sowie idempotenten Start und Stop. Diese Tests sind mit der Kategorie
`Contract` gekennzeichnet.
