# Lokale IPC – 2.0.81

Die Suite stellt die Named Pipe `CreatorControlSuite.CommandPipe.v1` bereit.
Sie verwendet `PipeOptions.CurrentUserOnly` und öffnet keinen Netzwerk-Port.

Befehle:

- system.ping
- system.status
- workflow.prepare
- workflow.countdown
- workflow.live
- workflow.pause
- workflow.resume
- workflow.end
- alert.test
- obs.scene

`CreatorControlSuite.CommandClient.exe` kann von Stream Deck oder lokalen
Automationen aufgerufen werden.
