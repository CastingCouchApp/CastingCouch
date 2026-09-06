# Tauri: Feature-Parität und Abnahme

Stand: 6. September 2026. Referenz ist der vom Nutzer ausgewählte C#-Funktionsumfang. **Die Umsetzung ist noch nicht abgeschlossen. Kein Cutover und keine vollständige Feature-Parität.**

## Verbindlicher Umfang

Workflow/Automatisierungsmodul, externe Steuerung/Multi-PC und kommerzielle Lizenzierung entfallen in Tauri. Workflow-Seite, Navigation, Sidecar-Commands, Supervisor und Shell-Abhängigkeit wurden entfernt. YouTube Music nutzt eine native Rust-HTTP-Bridge. Die vorhandenen WPF-Module und ihre Release-Strecke bleiben verfügbar. Historische Settings-Felder dieser Module werden ausschließlich zur verlustfreien Datenkompatibilität mitgeführt.

DA2 enthält keine Workflow-Steuerung. Die Workflow- und EX-Abhängigkeiten der ursprünglichen Planung entfallen. Musikautomationen MU4 bleiben ein eigenes gewünschtes Paket; MU5 betrifft vorerst interne Alerts. Lokale IPC aus den technischen Vorgaben bleibt als getrennte, noch offene Integrationsentscheidung sichtbar; daraus wird keine neue externe Bedienoberfläche abgeleitet.

## Statusbegriffe

- **Implementiert:** Funktion im Code und in automatisierten Prüfungen vorhanden. Dies ersetzt keine Betriebsabnahme.
- **Teilweise:** Mindestens ein relevanter Bestandteil fehlt.
- **Offen:** Im aktuellen Umsetzungsschritt nicht übernommen.
- Alle Pakete benötigen zusätzlich die vom Nutzer geforderte Abnahme mit installierten Windows- und macOS-Paketen. Deshalb bleiben die Abnahme-Checkboxen offen.

## Basis

| Abnahme | ID | Stand | Implementierung und verbleibende Arbeit |
|---|---|---|---|
| [ ] | B1 | Teilweise | `editorUrl`, `alertType`, `obsSceneName` korrigiert; UI-Regressionstests und sichtbarer Browser-Demomodus. Native IPC-Tests für Editor öffnen, Alert testen/löschen, Alert-Runtime, Canvas-Update und Settings ergänzt. Weitere Commands und generierte gemeinsame TS/Rust-Typen fehlen. |
| [ ] | B2 | Implementiert | Canvas-Build vor Packaging; HTML/JS/CSS/Assets in Rust-Binary eingebettet, kein Laufzeitzugriff auf das Repository. CI/Release installiert die Canvas-Abhängigkeiten. Installierte Pakete noch prüfen. |
| [ ] | B3 | Teilweise | Validierung, Port-Reservierung vor Commit, kontrollierter Austausch des Servers, Rücknahme bei Speicherfehler. Weitere Einstellungen müssen noch vollständig an die Dienste angebunden werden. |
| [ ] | B4 | Teilweise | Unbekannte verschachtelte Felder einschließlich identifizierter Listeneinträge erhalten; dreiwege Merge verhindert verlorene parallele Änderungen; vorhandene Enum-Konvertierung bleibt. Vollständige reale C#-Datenfixtures und sämtliche Listen-/Enum-Sonderfälle noch prüfen. |
| [ ] | B5 | Teilweise | Fehlgeschlagene Instanzsperre verhindert Start; EventSub-Abbruch löst Neuverbindung/Subscriptions aus; Token-Refresh vorhanden. Aktivierung der ersten Instanz, durchgängige sichtbare Startfehler und gesamte Wiederanlaufmatrix fehlen. |
| [ ] | B6 | Teilweise | Hello/Layout-Envelope, Chat-History, Assets/Extensions, Katalog/Presets und Live-OBS-Routen ergänzt. Alle Editor-Nachrichten, Konfigurationen und Solo-Varianten noch systematisch vergleichen. |
| [ ] | B7 | Teilweise | Periodischer Snapshot mit Musik, OBS, Streamstatus, Branding, Alerts und Countdown; Track-Events, persistente Chat-History. Twitch-Ziele, komplette Sessionstatistik und weitere Legacy-Felder fehlen. |

## Overlay

| Abnahme | ID | Stand | Implementierung und verbleibende Arbeit |
|---|---|---|---|
| [ ] | O1 | Implementiert | Erstellen/duplizieren/löschen, Auswahl und Umbenennen; IDs, URL und Layout beim Umbenennen erhalten; Persistenztest. |
| [ ] | O2 | Teilweise | Gemeinsamer vollständiger Canvas-Frontend-Build wird eingebettet. Backend-Verträge und alle Widgets benötigen noch gemeinsame Betriebsprüfung. |
| [ ] | O3 | Implementiert | Import, Auflistung, Anzeige/URL-Auswahl und echte Löschung; WPF-Indexformat und bestehende IDs, 15-MB-Limit; echter HTTP-Test. Bildinhalt wird wie bisher über Dateiendung akzeptiert. |
| [ ] | O4 | Teilweise | ZIP-Installation, Katalog, atomarer Austausch und Deinstallation; Pfad-/Datei-/Größen-/Referenzvalidierung; ungültiges Update erhält vorheriges Pack. Gesamte C#-Validierungsmatrix und alle Pack-Varianten noch abgleichen. |
| [ ] | O5 | Teilweise | Live-Videoeinstellungen und PNG-Screenshot aus OBS; Browserquellen-Assistent mit Canvas-/Szenen-/Namenswahl ergänzt. Erstellt oder aktualisiert Browserquellen mit Layout-Größe und aktueller URL. Fremde Quellentypen werden abgelehnt; vorhandene Position/Sichtbarkeit bleiben erhalten. WebSocket- und UI-Vertragstests vorhanden; Betriebsabnahme steht aus. |
| [ ] | O6 | Teilweise | Standalone-Chat-Frontend, Hintergrund und Konfiguration; Twitch-Fragments mit nativen Emotes, History und Moderationsbereinigung. Badge-Kataloge, BTTV/FFZ/7TV, alle Chat-Settings und Solo-/Canvas-Abnahme fehlen. |
| [ ] | O7 | Implementiert | Start/Stopp/Zeitänderung im Dashboard; Snapshot und WebSocket-Zustand für spät verbundene Clients; unabhängig vom Workflow. |

## OBS

| Abnahme | ID | Stand | Implementierung und verbleibende Arbeit |
|---|---|---|---|
| [ ] | OBS1 | Implementiert | Stream und Aufnahme starten/stoppen, Aufnahme pausieren/fortsetzen, Status in Dienste/Dashboard. |
| [ ] | OBS2 | Implementiert | Replay Buffer starten/stoppen/speichern und virtuelle Kamera. |
| [ ] | OBS3 | Teilweise | Typisierte Backend-Befehle für Profile, Sammlungen und Übergänge/Dauer; Auswahl-/Verwaltungsoberfläche fehlt. |
| [ ] | OBS4 | Teilweise | Backend-Befehle für Quellen, Scene Items, Transformation und Filter sowie Alert-Quelleneinrichtung. Vollständiger Editor fehlt. |
| [ ] | OBS5 | Teilweise | Backend für Mute, Lautstärke, Monitoring, Sync-Offset. Geräte-/Quellenanzeige und Audiobedienung fehlen. |
| [ ] | OBS6 | Teilweise | Regelmäßige Ausgangsstatus-/FPS-/CPU-Abfrage und Fehleranzeige. Optionale Ausgangsfehler werden einzeln angezeigt; Streamstatus bleibt erhalten. Bei fehlendem Status bleiben Schaltflächen deaktiviert. Umfassendes Monitoring fehlt. |

## Twitch

| Abnahme | ID | Stand | Implementierung und verbleibende Arbeit |
|---|---|---|---|
| [ ] | TW1 | Implementiert | Kanalinformationen, Titel/Kategorie setzen und Kategoriesuche. |
| [ ] | TW2 | Teilweise | EventSub Chat, Senden, Textanzeige und History; abgelehnte Sendebestätigung als Fehler. Reichhaltige App-Darstellung und vollständiger Ereignisfeed fehlen. |
| [ ] | TW3 | Teilweise | Eigenes Twitch-Popout-WebView; Windows mit eigenem persistenten Profil, macOS Standard-WebView-Speicher. Dauerhafter Login auf beiden Plattformen noch nachzuweisen. |
| [ ] | TW4 | Teilweise | Ban/Unban/Timeout/Delete-Backend, UI für Timeout/Löschen/Clear; EventSub-Synchronisierung. Vollständige Moderationsoberfläche fehlt. |
| [ ] | TW5 | Teilweise | Helix-Abfragen und einfache Anzeige für Follower/Subs/Chatter. Ziele, konsistente Viewer-Daten und Overlay-Ziele fehlen. |
| [ ] | TW6 | Teilweise | Kanal-/Live-/Followed-Suche und ausgehender Raid; Backend für Abbruch. Vollständiger Community-Ablauf noch prüfen. |
| [ ] | TW7 | Teilweise | Reward erstellen/anzeigen; Einlösungen laden und Status ändern. Reward bearbeiten/löschen und vollständiger Verwaltungsablauf fehlen. |
| [ ] | TW8 | Teilweise | Polls/Predictions erstellen, laden, beenden/auflösen. Vollständige C#-Optionen und Ereignissynchronisierung fehlen. |
| [ ] | TW9 | Offen | Chat-History ist vorhanden; Ereignis- und Streamhistorien sind noch nicht portiert. |

## Musik und Alerts

| Abnahme | ID | Stand | Implementierung und verbleibende Arbeit |
|---|---|---|---|
| [ ] | MU1 | Implementiert | Spotify Play/Pause, Vor/Zurück, Seek, Volume, Shuffle, Repeat; echte HTTP-Vertragstests, neue OAuth-Rechte bei erneuter Anmeldung. |
| [ ] | MU2 | Teilweise | Geräte anzeigen und Wiedergabe übertragen; persistente Gerätepräferenz fehlt. |
| [ ] | MU3 | Teilweise | Playlists/Titel, Suche, Play, Queue, Favoriten, zuletzt gehört. Gesamte Bibliotheks-/Paging-Parität noch prüfen. |
| [ ] | MU4 | Offen | Szenenmusik, Start/Ende und Fades fehlen. |
| [ ] | MU5 | Offen | Überlappungssicheres Alert-Ducking fehlt. Externe EX-Integrationen sind gestrichen. |
| [ ] | MU6 | Offen | Musikzustände, Profile, Historie und Wiederherstellung fehlen. |
| [ ] | MU7 | Offen | Musikstatistik fehlt. |
| [ ] | MU8 | Teilweise | Native Loopback-Bridge, Einrichtung/Bookmarklet, Frischeprüfung, Metadaten, unterstützte Befehle; echter HTTP-Test. Browserfreigaben und Betriebsabnahme auf Windows/macOS fehlen. |
| [ ] | MU9 | Teilweise | Providerwahl gespeichert, Metadaten/Cover/Fortschritt im Overlay-Snapshot; Anzeigeoptionen aus MusicPlayer. Vollständige gemeinsame Playerdarstellung und Einstellungen fehlen. |
| [ ] | AL1 | Teilweise | Text-/Medien-/Typ-/Dauer-/Font-/Farbe-/Positions-/Größeneditor und Textvorschau. Sound, Audio-Gerät, Ausschnitt, Animation und vollständige Medienvorschau fehlen. |
| [ ] | AL2 | Teilweise | Explizite OBS-Quelleneinrichtung, vorhandene Text-/Medienquellen abspielen/stoppen/ausblenden. Separate Soundwiedergabe, alle Layout-/Animationsdetails und zuverlässige Fehler-/Abbruchabnahme fehlen. |
| [ ] | AL3 | Teilweise | Aktiver Typ/Fehler, Stoppen, Queue leeren, aktivieren/deaktivieren, Zwischenpause. Fehler-/Stopprennen über echte OBS-Grenze noch testen. |
| [ ] | AL4 | Teilweise | Vollständige Event-Daten an Vorlagenausführung weitergereicht. Legacy-Variablennamen und Formatierung pro Ereignis müssen noch vollständig angeglichen werden. |

## Dashboard und System

| Abnahme | ID | Stand | Verbleibende Arbeit |
|---|---|---|---|
| [ ] | DA1 | Offen | Karten-/Szenenbutton-Konfiguration. |
| [ ] | DA2 | Teilweise | OBS-Ausgänge, Countdown und bisherige Statuskarten. Chat, Ereignisse, Kennzahlen und Musikbedienung im Bedienpult fehlen. Workflow entfällt. |
| [ ] | DA3 | Offen | Sessionerfassung, Persistenz und Auswertung. |
| [ ] | DA4 | Offen | Sessionanalyse, Creator Score und Wochenberichte. |
| [ ] | SYS1 | Offen | Ersteinrichtung mit Schritten, Prüfungen und Abschluss. |
| [ ] | SYS2 | Offen | Dokumentanzeige und versionierte Zustimmungen; getrennt von gestrichener kommerzieller Lizenzierung. |
| [ ] | SYS3 | Offen | App-Profile einschließlich Import/Export. |
| [ ] | SYS4 | Offen | Migration mit Vorschau/Backup und vollständige Wiederherstellung; Settings-Kompatibilität ist nur die Grundlage. |
| [ ] | SYS5 | Teilweise | Vorhandene Logs, Health und sichtbare Operationsfehler; Diagnoseoberfläche/Filter/API-Inspektor/Readiness fehlen. |
| [ ] | SYS6 | Teilweise | Bisherige Signaturprüfung, Download, Backup und Installerstart erhalten. Kein Nachweis abgeschlossener Windows-/macOS-Installation. |
| [ ] | SYS7 | Teilweise | Bestehende Theme-Tokens auf neuen Controls; durchgängige Anpassbarkeit und vollständige Branding-Parität fehlen. |

## Prüfungen und Grenzen

Automatisiert geprüft: Rust-Workspace, Frontend-Vitest, TypeScript und Produktions-Frontend-Build. Zusätzliche Tests betreffen Settings-Konflikte und unbekannte Daten, Canvas-Identität, eingebettete Distribution, Asset-/ZIP-Persistenz, echte Loopback-HTTP-Aufrufe, Chat-Fragments/History/Moderation, Countdown, Spotify-HTTP/OAuth und OBS-Request-Felder.

HTTP-Tests verwenden echte lokale Server mit temporären Daten. Spotify/Twitch-Tests verwenden kontrollierte lokale Gegenstellen; sie belegen keine Berechtigung oder Erreichbarkeit eines echten Accounts. OBS-Renderer hat WebSocket-Integrationstests für Playback/Stoppen ohne Quellenerstellung und sichtbare Fehler beim Ausblenden. Native Tauri-IPC prüft zentrale Commands einschließlich belegtem Server-Port und echter Persistenz; weitere Commands bleiben offen. Kein Live-Stream wurde ausgelöst.

Offen bleibt die gesamte Installations-/Betriebsabnahme auf Windows und macOS: Datenmigration, Verbindungen, OBS-Browserquellen, Chat/Musik/Alerts, Abbruch/Wiederanlauf, Neustart/Persistenz und Updateabschluss. WPF bleibt bis zu dieser Abnahme verfügbar.

## Nächste Umsetzungsschritte

1. Weitere native Command-Verträge und OBS-Playback mit Verbindungsabbrüchen/Stopprennen absichern; bestehende C#-Datenfixtures vollständig übernehmen.
2. B5–B7 und O5/O6 vervollständigen; vollständige Twitch-Daten-/Zielversorgung und Sessionerfassung als Grundlage für DA3/DA4.
3. OBS3–5-Oberflächen, Twitch-Verwaltung, Alert-Sound und Musikautomationen/Ducking/Zustände fertigstellen.
4. Dashboard, Einrichtung, Rechtstexte, Profile, Migration/Backups und Diagnostik umsetzen.
5. Windows-/macOS-Installer bauen und jeden gewählten Nutzerablauf dokumentiert abnehmen; erst danach Cutover entscheiden.


## Fortsetzung: OBS-Einrichtung und Vertragsprüfung

- O5: Assistent auf der Overlay-Seite; eine vorhandene Browserquelle wird nur bezüglich URL/Breite/Höhe aktualisiert. Existiert sie bereits in der Zielszene, wird kein zusätzliches Szenenelement erzeugt. OBS-Fehler werden an die Oberfläche weitergereicht.
- B1/B3: Tests laufen über `tauri::test::get_ipc_response` und die tatsächlichen Command-Makros, mit temporärem Settings-Verzeichnis. Der Tauri-Fenster-Runtime wird simuliert, die Argumentdeserialisierung und Fachlogik nicht. Portkonflikte werden mit einem real belegten lokalen Port geprüft.
- OBS6: Ein Fehler beim virtuellen Kamera-/Replay-/Aufnahmestatus vernichtet nicht mehr den erfolgreich gelesenen Streamstatus. Fehlerzustände werden nicht als „gestoppt“ ausgegeben.
- AL2/AL3: Stoppen führt alle Cleanup-Operationen aus; Fehler beim Stoppen/Ausblenden bleiben in der Runtime sichtbar. Die Tests verwenden eine lokale OBS-WebSocket-Gegenstelle und prüfen konkrete Requests.
- Windows: Common-Controls-v6-Manifest wird einmalig vom Linker für App und Tests eingebettet; Tauri-Ressourcen enthalten kein zweites Manifest. Keine Änderung am Berechtigungsniveau.

Validierung dieses Schritts: 154 Rust-Tests (Workspace einschließlich ergänzter nativer IPC-Prüfung), 47 Frontend-Tests sowie TypeScript-/Vite-Produktionsbuild. Die Windows-/macOS-Abnahme mit echten OBS-/Twitch-/Musikverbindungen und installiertem Paket bleibt offen.
