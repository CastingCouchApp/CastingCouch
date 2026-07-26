# 2.0.81 – E2E-Test

Die Suite besitzt einen bestätigungspflichtigen echten Workflow-E2E-Test:
Vorbereiten → Live → Pause → Fortsetzen → Ende.

Der Test nutzt den realen Workflow-Service. OBS und konfigurierte Dienste können tatsächlich gesteuert werden. Zusätzlich existiert `tests/e2e/Run-Workflow-E2E.ps1` für den CommandClient.
