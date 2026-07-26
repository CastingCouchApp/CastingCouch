# Creator Control Suite 8.0.0-alpha4

## Multi-PC Security & Power Controls

- Remote-Agent auf HTTPS/TLS mit dauerhaftem selbstsigniertem Gerätezertifikat umgestellt.
- SHA-256-Zertifikatsfingerabdruck wird beim Pairing geprüft und dauerhaft angeheftet.
- Status- und Befehlszugriffe akzeptieren danach ausschließlich das bekannte Gerätezertifikat.
- Gerätebezogene Befehlsfreigaben über `%LOCALAPPDATA%\CreatorControlSuite\Agent\agent-permissions.json`.
- Remote-Neustart und Remote-Herunterfahren ergänzt; standardmäßig nicht freigegeben.
- UI zeigt TLS-Vertrauen, Fingerabdruck und erlaubte Befehle.
- Agent-Version auf 8.0.0-alpha4 aktualisiert.
