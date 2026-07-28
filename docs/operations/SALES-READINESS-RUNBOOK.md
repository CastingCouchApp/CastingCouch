# Betriebs- und Verkaufsfreigabe

## Messbare Ziele

| Ablauf | Ziel | Messung |
|---|---:|---|
| Kaltstart bis Hauptfenster | ≤ 5 s (P95) | Windows-Release-Build, 30 Starts |
| Overlay-Event bis DOM-Update | ≤ 150 ms (P95) | Browser-Smoke-Timestamp |
| Agent-API im LAN | ≤ 500 ms (P95), Timeout 5 s | `samples.csv` |
| Update ohne Download | ≤ 5 min | Transaktionsjournal |
| Rollback nach Apply-Fehler | ≤ 2 min | Transaktionsjournal |
| Wiederverbindung nach LAN-Abbruch | ≤ 30 s | Soak-Protokoll |

## 24-Stunden-Soak

Voraussetzungen:

- signierter Release-Build auf einem Windows-Testsystem;
- OBS, Spotify/Twitch-Testkonten und Overlay-Browser-Source verbunden;
- Agent-Key und bestätigter TLS-Fingerprint nur als Prozessvariablen
  `CCS_SOAK_AGENT_KEY` und `CCS_SOAK_AGENT_FINGERPRINT`;
- keine produktiven Konten oder echten Zuschauerdaten.

Ausführung:

```powershell
./build/Invoke-SalesReadinessSoak.ps1 -DurationHours 24
```

Während des Laufs werden mindestens alle zwei Stunden OBS-Szenenwechsel,
Twitch-/Spotify-Reconnect, Overlay-Events, Agent-Netzwerkunterbrechung und ein
Suite-Neustart ausgelöst. `artifacts/soak/samples.csv` und `summary.json`
gehören zum Verkaufsfreigabe-Nachweis. Jeder Fehler oder fehlende Sample-Zeitraum
ist ein No-Go und erzeugt einen Eintrag im Risk Register.

## Installationsmatrix

Auf sauberen und bestehenden Windows-Installationen jeweils:

1. MSI-Signatur und signiertes Update-Manifest prüfen.
2. Installieren und First-Run abschließen.
3. Bestehende Profile/Secrets migrieren und auf Klartext prüfen.
4. Upgrade mit absichtlich gesperrter Datei abbrechen; Rollback prüfen.
5. Prozess während `Applying` beenden; Neustart muss Journal erkennen und
   zurückrollen.
6. Reguläres Upgrade, Start und Agent/Overlay-Smoke durchführen.
7. Deinstallieren; Nutzerdaten nur gemäß gewählter Uninstall-Option behandeln.

Freigabe erfordert Logs, Journale, SBOM, Hashes und die ausgefüllte
Release-Checkliste als unveränderliche CI-/Release-Artefakte.
