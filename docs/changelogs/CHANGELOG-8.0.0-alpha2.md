# Creator Control Suite 8.0.0-alpha2

## Multi-PC Remote Agent
- Neuer separater `CreatorControlSuite.Agent` mit HTTP-Heartbeat auf Port 47631.
- Gerätestatus: Rechnername, Agent-Version, RAM, Uptime sowie OBS-, Spotify- und Streamer.bot-Prozessstatus.
- Der Agent zeigt einen einmaligen sechsstelligen Pairing-Code an. Nach erfolgreicher Kopplung speichert die Suite einen zufälligen 256-Bit-Agentenschlüssel und überträgt ihn bei Remote-Anfragen.
- Remote-Aktionen für OBS starten/beenden, Spotify Play/Pause und Streamer.bot starten.
- Agent-Diagnose direkt im Multi-PC-Bereich.
- Ping-Fallback bleibt erhalten, wenn der Agent nicht installiert oder nicht erreichbar ist.

## Hinweis
Die Alpha verwendet HTTP im vertrauenswürdigen lokalen Netzwerk. TLS/Zertifikats-Pairing folgt in einer späteren Ausbaustufe.
