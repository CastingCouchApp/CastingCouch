# Creator Control Suite 8.0.0-alpha16

## Gestaffelte Multi-PC-Update-Rollouts

- Ein Update-ZIP kann nacheinander an alle gekoppelten Remote-Agenten verteilt werden.
- Pro Zielgerät werden Bereitstellung, Manifest-/Kompatibilitätsprüfung und Installation ausgeführt.
- Nur Agenten mit `updates.stage` und `updates.apply` nehmen am Rollout teil.
- Konfigurierbare Pause zwischen den Zielgeräten (0 bis 600 Sekunden).
- Live-Status pro Remote-PC im Multi-PC-Dashboard.
- Rollouts können abgebrochen werden; bereits gestartete Installationen werden nicht gewaltsam unterbrochen.
- Fehler auf einem Gerät stoppen nicht automatisch die Aktualisierung der übrigen Geräte.
- Agent-, Discovery- und Paketversion auf 8.0.0-alpha16 aktualisiert.
