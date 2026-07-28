# Verkaufsreife – Risk Register

Stand: 28. Juli 2026

| ID | Risiko | Schwere | Wahrscheinlichkeit | Owner | Abnahmekriterium | Status |
|---|---|---:|---:|---|---|---|
| SEC-01 | Agent-, Geräte- und TLS-Schlüssel ungeschützt | Kritisch | Hoch | Security/Agent | DPAPI-Migration, passwortgeschütztes PFX, Klartextbereinigung und Regressionstests | Behoben, Windows-E2E offen |
| SEC-02 | Pairing ohne Ablauf-/Versuchslimit | Hoch | Hoch | Security/Agent | POST v1, Ablaufzeit, Sperre, Rate Limit, Audit | Implementiert; 4-KiB-Limit, RFC-7807-Fehlervertrag und stabile Fehlercodes automatisiert geprüft; E2E offen |
| SEC-03 | Agent-Vertrauensaufbau nur per TOFU | Hoch | Mittel | Product/Security | Nutzerbestätigter Fingerprint aus Agent-Anzeige wird vor TLS-Verbindung gepinnt | Behoben, UX-E2E offen |
| UPD-01 | Archive/Backups können Zielpfade verlassen | Kritisch | Mittel | Update | Gemeinsamer Safe-ZIP-Extractor und Traversaltests | Behoben |
| UPD-02 | Teilabbruch und Rollback nicht auf Windows-E2E belegt | Hoch | Mittel | Release | Abbruchmatrix und verifizierter Rollback auf sauberer/bestehender Installation | Write-ahead-Journal und automatisierte Abbruch-/Retry-Matrix implementiert; harter Prozess-/Stromabbruch sowie MSI-E2E auf Windows offen |
| UPD-03 | Remote-Agent akzeptiert nicht release-signierte Updates | Kritisch | Mittel | Security/Update | RSA-Manifest, Paketgröße und SHA-256 vor Extraktion und Apply verifiziert | Behoben, Windows-E2E offen |
| ARC-01 | `MainWindow` als God Object | Hoch | Hoch | App Architecture | <500 LOC, vertikale Feature-Slices | Teilziel erreicht: `MainWindow.xaml` 943 Zeilen, `MainWindow.xaml.cs` 495 Zeilen und beide Größen CI-blockierend; Dashboard, Settings, Dienste, Workflow und Music als Views ausgelagert; Feature-Runtime, Event-Wiring, Navigation und Lifecycle in Partials unter 1.000 Zeilen getrennt. Regieplan-, Timed-Automation-, OBS-Dashboard-/Streamzustands-, Twitch-Raid-, Twitch-Historien-, Stream-Deck-Katalog-/Regel- sowie Streamer.bot-Protokollregeln liegen in testbaren Anwendungsservices. Run-of-Show ist in 622/375-Zeilen-Slices, OBS Connection/Streambeobachtung in 601/315-Zeilen-Slices, Timed Automation in 583/449-Zeilen-Slices, Twitch Dashboard in Chat/Metriken/Raid mit 190/276/437 Zeilen, Twitch Professional in Dashboard/Verbindung/Moderation mit 395/266/226 Zeilen, Stream Deck in Katalog/Transfer/Vorlagen/Regeln/Aktionen mit 582/197/243/478/358 Zeilen und Streamer.bot in Aktionen/Connection mit 454/466 Zeilen getrennt. Weitere UI-unabhängige Ablaufsteuerung bleibt zu verlagern. Multi-PC bleibt funktional unverändert und als Alpha zurückgestellt |
| ARC-02 | Große Protokoll-/Agent-/Overlay-Dateien | Mittel | Hoch | Module | Bestehende Baseline sinkt; keine neue Produktionsdatei ≥1.000 LOC | Behoben: Integrationsdateien unter 1.000 Zeilen; Agent-Composition-Root auf 128 Zeilen und vier Endpunktgruppen reduziert; Overlay-Runtime auf 719 Zeilen reduziert und TypeScript in das CI-Größengate aufgenommen |
| PROD-01 | Multi-PC-Oberfläche noch nicht produktionsreif | Mittel | Hoch | Product/App | Sichtbar als Alpha gekennzeichnet; keine Stabilitätszusage bis gesonderter E2E-Freigabe | Bewusst zurückgestellt |
| QA-01 | Fehlende Protokoll-, Agent- und UI-Integrationstests | Hoch | Hoch | QA/Engineering | Contract-/Integrationstests und Coverage-Gates | Globale Coverage-Ratsche und CI-blockierendes 90-%-Branch-Gate für kritische Dateien implementiert; `FileUpdateTransaction` erreicht 100 % Branch-Coverage. Agent-Routenverträge, 401-/403-Ausführung, RFC-7807-Fehler, Payload-Limits und Secret-Leakage sowie Overlay/Update, Named Pipe und Spotify-, Twitch- und OBS-Verträge sind geprüft; reale Plattform-E2E weiter offen |
| REL-01 | Kein SBOM-/Provenance-/Dependency-Gate | Hoch | Mittel | Release | CI-Artefakte und verpflichtende Security-Jobs | Implementiert, erster CI-Nachweis offen |
| OPS-01 | Redaction und Support-Paket nur teilweise nachgewiesen | Hoch | Mittel | Operations | Positivliste und Secret-Redaction-Tests | Behoben |
| COM-01 | Open-Source-Lizenz und Legal-Texte nicht final | Kritisch | Hoch | Product/Legal | Root-LICENSE, Drittanbieter-Notices und freigegebene Rechtstexte liegen vor | Laufzeit-Lizenzierung entfernt, Entscheidung extern blockiert |
| COM-02 | Spotify-Richtlinien können Streaming-, Overlay- und kommerzielle Nutzung untersagen oder einschränken | Kritisch | Hoch | Product/Legal | Schriftliche Freigabe bzw. belastbares Compliance-Konzept; andernfalls Spotify aus der freigegebenen Distribution entfernen | Offen, Verkaufsblocker |

Verkaufsfreigabe ist nur zulässig, wenn keine offenen kritischen oder hohen
Risiken bestehen oder eine dokumentierte, zeitlich begrenzte Risikoakzeptanz
durch Product und Security vorliegt.
