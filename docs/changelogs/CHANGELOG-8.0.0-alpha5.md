# Creator Control Suite 8.0.0-alpha5

## Remote-OBS-Steuerung
- Remote-Szenenliste und aktuelle Programmszene über den Agenten abrufen.
- Programmszene auf einem gekoppelten Streaming-PC wechseln.
- OBS-Audioquellen laden sowie muten und entmuten.
- OBS-WebSocket-Host, Port und Passwort direkt aus der Haupt-Suite an den Agenten übertragen.
- Neue Agent-Berechtigung `obs.control`; standardmäßig freigegeben.
- Agent und Suite auf Version 8.0.0-alpha5 aktualisiert.

## Sicherheit
- Alle neuen Endpunkte bleiben durch TLS-Fingerabdruck und Geräteschlüssel geschützt.
- Remote-OBS-Befehle werden nur mit der Berechtigung `obs.control` akzeptiert.
