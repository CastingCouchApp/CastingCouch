# Creator Control Suite 8.0.0-alpha4

## Multi-PC Discovery & Wake-on-LAN

- automatische Agent-Erkennung im lokalen Netzwerk über UDP Port 47632
- gefundener Agent wird direkt in das Kopplungsformular übernommen
- MAC-Adresse wird beim Pairing gespeichert
- Wake-on-LAN über Broadcast-Port 9
- Agent meldet Rechnername, Version, Port und MAC-Adresse

## Remote-Aktionshistorie

- die Suite zeigt die letzten Remote-Aktionen der aktuellen Sitzung
- der Agent protokolliert die letzten 100 angenommenen oder fehlgeschlagenen Befehle
- neuer geschützter Endpunkt `/api/history`

## Konfigurierbare Startpfade

Neue Agent-Datei:

`%LOCALAPPDATA%\CreatorControlSuite\Agent\agent-settings.json`

Darin können vollständige Pfade für OBS und Streamer.bot hinterlegt werden. Ohne Eintrag verwendet der Agent weiterhin die bekannten Programmnamen.

## Sicherheit

- LAN-Erkennung liefert keine Geräteschlüssel und führt keine Befehle aus
- Status, Historie und Aktionen bleiben TLS- und schlüsselgeschützt
- Wake-on-LAN benötigt eine explizit gespeicherte MAC-Adresse
