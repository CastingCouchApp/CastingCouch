# Creator Control Suite 8.0.0-alpha17

## Geplante Rollouts und Wartungsfenster

- Update-Rollouts können für einen zukünftigen Zeitpunkt geplant werden.
- Unterstützt Eingaben wie `morgen 02:00` sowie lokale Datums- und Zeitangaben.
- Das Update-Paket wird bereits bei der Planung ausgewählt.
- Eine bestehende Planung kann vor dem Start aufgehoben werden.
- Optionales Wartungsfenster mit Start- und Endzeit.
- Zeitfenster über Mitternacht werden unterstützt, zum Beispiel 22:00 bis 05:00.
- Außerhalb des Wartungsfensters pausiert der Rollout kontrolliert und setzt sich automatisch fort.
- Statusanzeige für geplante, wartende und ausgeführte Rollouts.
- Agent- und Discovery-Version auf 8.0.0-alpha17 aktualisiert.

## Sicherheit

- Bereits laufende Geräteinstallationen werden beim Aufheben einer Planung nicht unterbrochen.
- Die bestehende Canary- und Fehlerquotenlogik bleibt aktiv.
