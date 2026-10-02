# Tauri: Feature-Parität und Abnahme

Stand: 3. Oktober 2026. Referenz ist der vom Nutzer ausgewählte C#-Funktionsumfang. **Die Umsetzung ist noch nicht abgeschlossen. Kein Cutover und keine vollständige Feature-Parität.**

## Verbindlicher Umfang

Workflow/Automatisierungsmodul, externe Steuerung/Multi-PC und kommerzielle Lizenzierung entfallen in Tauri. Workflow-Seite, Navigation, Sidecar-Commands, Supervisor und Shell-Abhängigkeit wurden entfernt. YouTube Music nutzt eine native Rust-HTTP-Bridge. Die vorhandenen WPF-Module und ihre Release-Strecke bleiben verfügbar. Historische Settings-Felder dieser Module werden ausschließlich zur verlustfreien Datenkompatibilität mitgeführt.

DA2 enthält keine Workflow-Steuerung. Die Workflow- und EX-Abhängigkeiten der ursprünglichen Planung entfallen. Musikautomationen MU4 bleiben ein eigenes gewünschtes Paket; MU5 betrifft vorerst interne Alerts. Lokale IPC aus den technischen Vorgaben ist als Named Pipe unter Windows und Unix-Socket unter macOS umgesetzt; daraus wird keine neue externe Bedienoberfläche abgeleitet.

## Statusbegriffe

- **Implementiert:** Funktion im Code und in automatisierten Prüfungen vorhanden. Dies ersetzt keine Betriebsabnahme.
- **Teilweise:** Mindestens ein relevanter Bestandteil fehlt.
- **Offen:** Im aktuellen Umsetzungsschritt nicht übernommen.
- Alle Pakete benötigen zusätzlich die vom Nutzer geforderte Abnahme mit installierten Windows- und macOS-Paketen. Deshalb bleiben die Abnahme-Checkboxen offen.

## Basis

| Abnahme | ID | Stand | Implementierung und verbleibende Arbeit |
|---|---|---|---|
| [ ] | B1 | Implementiert | Command-Namen und Argumente sowie OBS-/Twitch-/Spotify-Enums werden aus Rust nach TypeScript erzeugt; Build prüft Drift und falsche Aufrufe. Native IPC prüft zentrale Persistenz-, Port-, Start-, Editor-, Alert- und Chat-Verträge. Browser-Demobetrieb bleibt gekennzeichnet. |
| [ ] | B2 | Implementiert | Canvas-Build vor Packaging; HTML/JS/CSS/Assets im Rust-Binary eingebettet. Kein Repository-Zugriff zur Laufzeit. CI baut jetzt NSIS/MSI und DMG; Installation und Live-Abnahme bleiben separat offen. |
| [ ] | B3 | Implementiert | Validierung, Port-Reservierung, Austausch und Rücknahme bei Speicherfehler; alter Server schließt WebSockets. Laufzeitstatus und URLs aktualisiert. OBS-/Twitch-/Spotify-Verbindungsparameter werden angewendet; neue OBS-Zugangsdaten vor der Neuverbindung gespeichert. Teilerfolge werden als gespeicherte Einstellungen mit konkreten Warnungen gemeldet. Nicht portierte Komfortoptionen sind deaktiviert gekennzeichnet. |
| [ ] | B4 | Implementiert | C#-Default- und mit Listeneinträgen befüllte Modell-Fixtures werden feldweise auf verlustfreien Roundtrip geprüft. Unbekannte verschachtelte Felder und Eintragsidentitäten sowie unveränderte Enum-/Zahlendarstellungen bleiben erhalten. Dreiwege-Merge übernimmt unabhängige Änderungen und meldet Konflikte. |
| [ ] | B5 | Implementiert | Instanzsperre über einen zweiten echten Prozess geprüft (auch Ok(false) wird abgewiesen). Startfehler erscheinen in der App; ausgefallener Overlay-Server wird erneut gestartet. EventSub-Abbruch und erneute Subscriptions, sichtbare Verbindungsfehler sowie serialisierte Token-Erneuerung geprüft. Erste Instanz wird nicht automatisch fokussiert; zweite Instanz wird verhindert. |
| [ ] | B6 | Implementiert | C#-Routen abgeglichen: Hello mit Canvas-Katalog, ausgewählter Canvas bei /editor und /view, tatsächlicher Health-Port, Layout-Defaults und persistente WebSocket-Änderungen. Chat-Konfiguration/History, Assets/Extensions, Katalog/Presets und OBS-Antworten angebunden. Weitere Widget-/Pack-Betriebsabnahme gehört zu O2/O4/O6. |
| [ ] | B7 | Implementiert | Vollständige C#-Snapshot-Felder mit definierten Leer-/Verfügbarkeitszuständen. Unabhängige OBS-/Twitch-Abfragen, gemeinsamer Musik-/Stream-/Alert-/Branding-/Countdown-Datenstand, Twitch-Ziele und laufende Sitzungszähler. Datei und HTTP erhalten benutzerdefinierte Zusatzfelder; konfigurierte Datenpfade und Hardlinks bleiben nutzbar. Historische Sessionberichte bleiben DA3/DA4. |

Implementierungsnachweise und verbleibende Betriebsabnahme: [Basis-Abnahme](TAURI-BASE-ACCEPTANCE.md). Die offenen Checkboxen bezeichnen die plattformübergreifende Abnahme, nicht fehlende Basisimplementierung.

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
| [ ] | OBS3 | Teilweise | Typisierte Abfragen und Bedienoberfläche für Profile, Szenensammlungen, Übergänge und Dauer vorhanden. Betriebsabnahme mit OBS steht aus. |
| [ ] | OBS4 | Teilweise | Quellen-/Gruppenauswahl, Sichtbarkeit, Sperre, Reihenfolge, Transformation und Filter-Schalter bedienbar. Häufige Quellenparameter können geändert werden; andere Einstellungen bleiben erhalten. Vollständige Filterparameter- und quellentypspezifische Editoren fehlen. |
| [ ] | OBS5 | Teilweise | Quellenauswahl mit tatsächlichem Mute-, Lautstärke-, Monitoring- und Sync-Offset-Zustand sowie Bedienung vorhanden. Nicht unterstützte Audioabfragen bleiben als Fehler sichtbar. Betriebsabnahme steht aus. |
| [ ] | OBS6 | Teilweise | Regelmäßige Ausgangsstatus-/FPS-/CPU-Abfrage und Fehleranzeige. Optionale Ausgangsfehler werden einzeln angezeigt; Streamstatus bleibt erhalten. Bei fehlendem Status bleiben Schaltflächen deaktiviert. Umfassendes Monitoring fehlt. |

## Twitch

| Abnahme | ID | Stand | Implementierung und verbleibende Arbeit |
|---|---|---|---|
| [ ] | TW1 | Implementiert | Kanalinformationen, Titel/Kategorie setzen und Kategoriesuche. |
| [ ] | TW2 | Teilweise | EventSub Chat, Senden, Textanzeige und History; abgelehnte Sendebestätigung als Fehler. Reichhaltige App-Darstellung und vollständiger Ereignisfeed fehlen. |
| [ ] | TW3 | Teilweise | Eigenes Twitch-Popout-WebView; Windows mit eigenem persistenten Profil, macOS Standard-WebView-Speicher. Dauerhafter Login auf beiden Plattformen noch nachzuweisen. |
| [ ] | TW4 | Teilweise | Ban/Unban/Timeout/Delete-Backend, UI für Timeout/Löschen/Clear; EventSub-Synchronisierung. Vollständige Moderationsoberfläche fehlt. |
| [ ] | TW5 | Teilweise | Helix-Abfragen und einfache Anzeige für Follower/Subs/Chatter. B7 versorgt Viewer-Daten und vorhandene Zielkonfiguration im Overlay; der vollständige Ziele-Editor fehlt. |
| [ ] | TW6 | Teilweise | Kanal-/Live-/Followed-Suche und ausgehender Raid; Backend für Abbruch. Vollständiger Community-Ablauf noch prüfen. |
| [ ] | TW7 | Implementiert | Rewards erstellen/bearbeiten/pausieren/fortsetzen/löschen; Titel, Kosten, Beschreibung, Farbe, Aktivierung und Eingabepflicht. Teiländerungen erhalten übrige Twitch-Felder. Einlösungen mit Zuschauereingabe, Statusfilter, Paging und Erfüllen/Erstatten; externe Änderungen über EventSub und periodische Abfrage. Twitch-Ownership-/Berechtigungsfehler bleiben sichtbar. Live-Abnahme offen. |
| [ ] | TW8 | Implementiert | Getrennte Umfragen-/Vorhersagenverwaltung mit Ergebnissen, Paging, C#-Normalisierung und Zeitgrenzen. Umfragen beenden/archivieren; Vorhersagen sperren/auflösen/abbrechen, Gewinner bestätigen. EventSub-Beginn/Fortschritt/Ende aktualisiert die UI; periodische Abfragen decken verlorene Events ab. Live-Abnahme offen. |
| [ ] | TW9 | Offen | Chat-History ist vorhanden; Ereignis- und Streamhistorien sind noch nicht portiert. |

## Musik und Alerts

| Abnahme | ID | Stand | Implementierung und verbleibende Arbeit |
|---|---|---|---|
| [ ] | MU1 | Implementiert | Spotify Play/Pause, Vor/Zurück, Seek, Volume, Shuffle, Repeat; echte HTTP-Vertragstests, neue OAuth-Rechte bei erneuter Anmeldung. |
| [ ] | MU2 | Implementiert | Geräte anzeigen, eingeschränkte Geräte kennzeichnen, Wiedergabe übertragen und Standardgerät persistent auswählen/aktivieren. Konfigurierbarer Rückfall auf aktives/steuerbares Gerät; automatisches Aktivieren beim Start von Titeln/Playlists. Player-Befehle und manuelle Lautstärke verwenden die Präferenz; Alert-Restaurierung bleibt am ursprünglichen Gerät. Live-Abnahme offen. |
| [ ] | MU3 | Teilweise | Playlists/Titel, Suche, Play, Queue, Favoriten, zuletzt gehört. Gesamte Bibliotheks-/Paging-Parität noch prüfen. |
| [ ] | MU4 | Offen | Szenenmusik, Start/Ende und Fades fehlen. |
| [ ] | MU5 | Implementiert | Native Alert-Engine senkt Spotify ab und hält die Lautstärke über überlappende Aktivitäten und die Queue. Wiederherstellung erfolgt auf dem ursprünglichen Gerät; manuelle Änderungen werden als neue Ziellautstärke übernommen. Abbruch, HTTP-Fehler und Retry sind getestet. Einstellungen auf der Musikseite; externe EX-Integrationen entfallen. Betriebsabnahme bleibt offen. |
| [ ] | MU6 | Offen | Musikzustände, Profile, Historie und Wiederherstellung fehlen. |
| [ ] | MU7 | Offen | Musikstatistik fehlt. |
| [ ] | MU8 | Teilweise | Native Loopback-Bridge, Einrichtung/Bookmarklet, Frischeprüfung, Metadaten, unterstützte Befehle; echter HTTP-Test. Browserfreigaben und Betriebsabnahme auf Windows/macOS fehlen. |
| [ ] | MU9 | Teilweise | Providerwahl gespeichert, Metadaten/Cover/Fortschritt im Overlay-Snapshot; Anzeigeoptionen aus MusicPlayer. Vollständige gemeinsame Playerdarstellung und Einstellungen fehlen. |
| [ ] | AL1 | Teilweise | Namen/Typen, Text, Medien, Soundpfad, Ausschnitt, Dauer, Priorität, Schrift, Farbe, Position, Größe und Animationsauswahl bedienbar. Native lokale Medien-/Soundvorschau mit begrenzter Dateigröße und Fehleranzeige; Umbenennung erhält Zusatzfelder. Auswahl des tatsächlichen Audioausgangs und animierte räumliche Vorschau fehlen. |
| [ ] | AL2 | Teilweise | Explizite OBS-Quelleneinrichtung, vorhandene Text-/Medienquellen abspielen/stoppen/ausblenden. Text-/Medienregionen entsprechen jetzt dem C#-Layout. Separate Soundwiedergabe und OBS-Animationen fehlen; Fehler-/Abbruchtests nutzen echte lokale WebSocket-Grenzen, Betriebsabnahme steht aus. |
| [ ] | AL3 | Implementiert | Aktiven Typ und Fehler anzeigen, stoppen, Queue leeren, aktivieren/deaktivieren sowie Queue-Kapazität und Zwischenpause speichern. Deaktivieren stoppt den aktuellen Alert und verwirft die Queue. App-Ende wartet auf Cleanup einschließlich Musikrestaurierung; Fehler und Stoppen über lokale OBS-/Spotify-Gegenstellen geprüft. |
| [ ] | AL4 | Implementiert | Follow/Sub/ReSub/GiftSub/Cheer/Raid verwenden sämtliche originalen Felder; ReSub/GiftSub erhalten zusätzlich die in C#-Vorlagen verwendeten months/count-Aliase und lesbare message_text-Ergänzung. Anonyme Nutzer und Testvariablen berücksichtigt. Groß-/Kleinschreibung und unbekannte Platzhalter bleiben kompatibel. Overlay-Alert-Ereignisse enthalten gerenderten Text und Variablen. |

## Dashboard und System

| Abnahme | ID | Stand | Verbleibende Arbeit |
|---|---|---|---|
| [ ] | DA1 | Offen | Karten-/Szenenbutton-Konfiguration. |
| [ ] | DA2 | Teilweise | OBS-Ausgänge, Countdown und bisherige Statuskarten. Chat, Ereignisse, Kennzahlen und Musikbedienung im Bedienpult fehlen. Workflow entfällt. |
| [ ] | DA3 | Offen | Sessionerfassung, Persistenz und Auswertung. |
| [ ] | DA4 | Offen | Sessionanalyse, Creator Score und Wochenberichte. |
| [ ] | SYS1 | Offen | Ersteinrichtung mit Schritten, Prüfungen und Abschluss. |
| [ ] | SYS2 | Offen | Dokumentanzeige und versionierte Zustimmungen; getrennt von gestrichener kommerzieller Lizenzierung. |
| [ ] | SYS3 | Implementiert | Profile aus gespeicherten Einstellungen erstellen, Metadaten bearbeiten, anwenden, löschen und über native Dateidialoge importieren/exportieren. C#-Dateiformat und vorhandener Profilordner bleiben verwendbar; Konflikte und belegte Ports brechen das Anwenden ohne Änderung ab. Installierte Betriebsabnahme steht aus. |
| [ ] | SYS4 | Offen | Migration mit Vorschau/Backup und vollständige Wiederherstellung; Settings-Kompatibilität ist nur die Grundlage. |
| [ ] | SYS5 | Teilweise | Vorhandene Logs, Health und sichtbare Operationsfehler; Diagnoseoberfläche/Filter/API-Inspektor/Readiness fehlen. |
| [ ] | SYS6 | Teilweise | Bisherige Signaturprüfung, Download, Backup und Installerstart erhalten. Kein Nachweis abgeschlossener Windows-/macOS-Installation. |
| [ ] | SYS7 | Teilweise | Bestehende Theme-Tokens auf neuen Controls; durchgängige Anpassbarkeit und vollständige Branding-Parität fehlen. |

## Prüfungen und Grenzen

Automatisiert geprüft: Rust-Workspace, Frontend-Vitest, TypeScript und Produktions-Frontend-Build. Zusätzliche Tests betreffen Settings-Konflikte und unbekannte Daten, Canvas-Identität, eingebettete Distribution, Asset-/ZIP-Persistenz, echte Loopback-HTTP-Aufrufe, Chat-Fragments/History/Moderation, Countdown, Spotify-HTTP/OAuth und OBS-Request-Felder.

HTTP-Tests verwenden echte lokale Server mit temporären Daten. Spotify/Twitch-Tests verwenden kontrollierte lokale Gegenstellen; sie belegen keine Berechtigung oder Erreichbarkeit eines echten Accounts. OBS-Renderer hat WebSocket-Integrationstests für Playback/Stoppen ohne Quellenerstellung und sichtbare Fehler beim Ausblenden. Native Tauri-IPC prüft zentrale Commands einschließlich belegtem Server-Port und echter Persistenz; weitere Commands bleiben offen. Kein Live-Stream wurde ausgelöst.

Offen bleibt die gesamte Installations-/Betriebsabnahme auf Windows und macOS: Datenmigration, Verbindungen, OBS-Browserquellen, Chat/Musik/Alerts, Abbruch/Wiederanlauf, Neustart/Persistenz und Updateabschluss. WPF bleibt bis zu dieser Abnahme verfügbar.

## Nächste Umsetzungsschritte

1. Installierte Pakete und echte Dienstverbindungen abnehmen; weitere Alert-Playback-Abbruchfälle im Feature-Paket AL2/AL3 prüfen.
2. O4/O6, vollständige Twitch-Oberflächen und Sessionerfassung für DA3/DA4 fertigstellen.
3. Verbleibende OBS-Quellen-/Filtereditoren, Twitch-Verwaltung, Alert-Sound und Szenenmusik und Musikzustände fertigstellen.
4. Dashboard, Einrichtung, Rechtstexte, Migration/Backups und Diagnostik umsetzen; Profile im installierten Paket abnehmen.
5. Windows-/macOS-Installer bauen und jeden gewählten Nutzerablauf dokumentiert abnehmen; erst danach Cutover entscheiden.


## Fortsetzung: OBS-Einrichtung und Vertragsprüfung

- O5: Assistent auf der Overlay-Seite; eine vorhandene Browserquelle wird nur bezüglich URL/Breite/Höhe aktualisiert. Existiert sie bereits in der Zielszene, wird kein zusätzliches Szenenelement erzeugt. OBS-Fehler werden an die Oberfläche weitergereicht.
- B1/B3: Tests laufen über `tauri::test::get_ipc_response` und die tatsächlichen Command-Makros, mit temporärem Settings-Verzeichnis. Der Tauri-Fenster-Runtime wird simuliert, die Argumentdeserialisierung und Fachlogik nicht. Portkonflikte werden mit einem real belegten lokalen Port geprüft.
- OBS6: Ein Fehler beim virtuellen Kamera-/Replay-/Aufnahmestatus vernichtet nicht mehr den erfolgreich gelesenen Streamstatus. Fehlerzustände werden nicht als „gestoppt“ ausgegeben.
- AL2/AL3: Stoppen führt alle Cleanup-Operationen aus; Fehler beim Stoppen/Ausblenden bleiben in der Runtime sichtbar. Die Tests verwenden eine lokale OBS-WebSocket-Gegenstelle und prüfen konkrete Requests.
- Windows: Common-Controls-v6-Manifest wird einmalig vom Linker für App und Tests eingebettet; Tauri-Ressourcen enthalten kein zweites Manifest. Keine Änderung am Berechtigungsniveau.

Validierung dieses Schritts: 154 Rust-Tests (Workspace einschließlich ergänzter nativer IPC-Prüfung), 47 Frontend-Tests sowie TypeScript-/Vite-Produktionsbuild. Die Windows-/macOS-Abnahme mit echten OBS-/Twitch-/Musikverbindungen und installiertem Paket bleibt offen.

## Fortsetzung: OBS-Verwaltung

Die Dienste-Seite bietet jetzt Profile, Szenensammlungen und Übergänge, Scene Items einschließlich verschachtelter Gruppen sowie Audioeinstellungen. Transformationswerte werden über eine separate OBS-Abfrage geladen. Änderungen einzelner Quellenparameter verwenden ausdrücklich `overlay: true`, damit übrige Einstellungen erhalten bleiben. Nach Änderungen werden die betroffenen Abfragen aktualisiert; eine manuelle Aktualisierung ist ebenfalls verfügbar.

Neue Tests prüfen echte Tauri-Command-Deserialisierung, OBS-WebSocket-Abfragen und Fehlerantworten sowie UI-Auswahl, Mute, Sichtbarkeit und gezielte Transformationsänderungen. Die kontrollierte WebSocket-Gegenstelle ersetzt keine Live-Abnahme. Vollständige Filterparameter, sämtliche quellentypspezifischen Einstellungen und laufende Synchronisierung externer OBS-Änderungen bleiben offen.

Validierung: 157 Rust-Tests, 51 Frontend-Tests und TypeScript-/Vite-Build erfolgreich. Windows-/macOS-Installations- und Live-Abnahme bleiben offen.

## Fortsetzung: Basis B1–B7

Die technische Basis ist implementiert. Die Instanzsperre wurde durch einen Test mit einem zweiten Prozess korrigiert; der vorherige Stand wertete `Ok(false)` fälschlich als Erfolg. Weitere gefundene und behobene Fehler: Editor-WebSocket-Frames ohne Speicherung, unvollständiges Hello, falsche Layout-Defaults und Health-Ports, veränderte C#-Wertdarstellungen, parallele Token-Erneuerungen und verlorene Hardlinks bzw. Zusatzfelder bei der Datenversorgung.

Nicht portierte Desktop-Komfortoptionen und Drittanbieter-Emotes werden in den Einstellungen als noch nicht verfügbar angezeigt. Diese Feature-Arbeit bleibt in SYS7/O6; sie wird nicht als abgeschlossen gezählt. Windows-/macOS-Installation, echte OAuth-/OBS-Verbindungen und ein vollständiger Streamablauf sind weiterhin nicht abgenommen.

## Fortsetzung: Alerts, Musikabsenkung und CI

Die Alert-Engine ist mit einem nativen Spotify-Ducking-Koordinator verbunden. Er serialisiert Volume-Anfragen, erhält den ursprünglichen Gerätebezug und speichert einen offenen Wiederherstellungsauftrag auch bei Abbruch oder HTTP-Fehler. Der Watchdog versucht fehlgeschlagene Wiederherstellungen erneut. Lautstärkeabsenkung erhöht keine bereits leisere Wiedergabe. Die gemeinsame Fachlogik wird von UI-Commands und Alert-Worker verwendet; keine EX-Integration wird wieder eingeführt.

Neue Regressionstests fanden und beheben: weiterlaufende deaktivierte Alerts, fehlende Medien-Transformation und verlorene Zusatzfelder beim Umbenennen. Die Vorschau liest lokale Dateien nur auf ausdrücklichen Vorschauaufruf, begrenzt sie auf 64 MB und verwendet dieselbe Textvorlage wie die Engine. Soundwiedergabe während des echten Alerts und Audioausgangsauswahl bleiben AL1/AL2.

Der letzte CI-Lauf auf main (`fe55c78`, Run 34040850347) bestätigte Windows-Build/Packaging, scheiterte aber bei macOS-IPC-Tests am Windows-spezifischen Invoke-Origin. Die Tests verwenden jetzt die tatsächliche WebView-URL. Drei bestehende WPF-Testannahmen wurden an den aktuellen Vertrag angepasst (Sidecar-Feld, kompakte Breite 1120, plattformunabhängige Zeilenenden). Der Canvas-Lockfile erhält mit npm 11 behebbare Updates innerhalb der bestehenden Versionsbereiche; npm audit meldet lokal keine Schwachstellen. Kein Tauri-Cutover und keine Live-/Installationsfreigabe.

Lokale Validierung dieses Schritts: 192 Rust-Tests (190 im gesamten Workspace plus die danach ergänzten Prüfungen aller sechs Twitch-Alerttypen und der Deaktivierung über die allgemeinen Einstellungen), 59 Tauri-Frontend-Tests, 98 Canvas-Tests, 600 C#-Tests sowie TypeScript- und Produktionsbuilds erfolgreich. Die aktualisierten macOS-IPC-Prüfungen müssen durch den neuen CI-Lauf bestätigt werden.

## Fortsetzung: Twitch-Verwaltung TW7/TW8

Die Dienste-Seite nutzt eigenständige Reward-, Umfragen- und Vorhersagenkomponenten mit typisierten Rust-Actions. PATCH für Rewards überträgt ausschließlich angegebene Felder und erhält Limits oder andere Optionen. Einlösungen können nach offen/erfüllt/erstattet gefiltert und mit Cursor abgefragt werden; die Sortierung entspricht C# (`OLDEST`). Fehlgeschlagene Änderungen behalten den Editorentwurf und zeigen den API-Fehler.

Umfragen und Vorhersagen haben getrennte Entwürfe, Grenzprüfung und Ergebnisse. Der Backend-Vertrag trimmt Texte und begrenzt Zeitwerte wie C#. Ungültige Statuswechsel und fehlende Gewinner werden vor OAuth-/Netzwerkzugriff abgelehnt. Zwölf zusätzliche EventSub-Abonnements aktualisieren Rewards, Einlösungen, Umfragen und Vorhersagen; Fortschrittsbursts werden in React zusammengefasst. Wiederverbindung verwendet dieselbe Subscription-Liste. Keine Workflow- oder EX-Funktion wurde eingeführt.

Verträge abgeglichen mit dem C#-Client sowie [Twitch Helix](https://dev.twitch.tv/docs/api/reference/) und [EventSub-Typen](https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/). Tests prüfen konkrete Loopback-HTTP-Anfragen, 204-Löschung, Ownership-Fehler, Paging, echte WebSocket-Notifications und native Tauri-Argumentdeserialisierung. Lokal bestehen 199 Rust-Tests, 63 Frontend-Tests und TypeScript-/Vite-Build. Keine echte Twitch-Belohnung, Umfrage oder Vorhersage wurde verändert.

Der CI-Lauf für `9c3e681` (Run 37074486729) bestätigt inzwischen Rust-Tests auf Windows und macOS, .NET-Tests, Architektur, Overlay und Dependency-Audit. Die Installer-Builds laufen zum Dokumentationszeitpunkt noch. Gesamte Feature-Parität und plattformübergreifende Betriebsabnahme bleiben offen.

## Fortsetzung: Spotify-Geräte MU2

Die Musikseite bietet eine gespeicherte Gerätepräferenz, Aktivierung ohne erzwungenen Wiedergabestart, manuelle Übertragung und die vorhandenen C#-Optionen `AutoTransferToPreferredDevice` und `UseActiveDeviceWhenPreferredUnavailable`. Nicht erreichbare Präferenzen bleiben gespeichert und sichtbar. Eingeschränkte Geräte werden bei Aktivierung abgewiesen. Die Auswahl-/Rückfallreihenfolge entspricht `SpotifyModule.ActivatePreferredDeviceAsync`.

Der Rust-Client bindet Player-Befehle an das konfigurierte Gerät. Beim Titel-/Playliststart mit aktivierter Transferoption wird zuerst das Gerät ausgewählt und aktiviert; Bibliotheksbefehle erhalten keine Geräteparameter. Die Volume-Fachlogik berücksichtigt die Präferenz, während aktive Alert-Absenkungen ihren ursprünglichen Gerätebezug behalten. Einstellungen werden über den vorhandenen Dreiwege-Merge gespeichert; übrige Spotify-Optionen bleiben erhalten.

Fünf zusätzliche echte Loopback-HTTP-Tests prüfen expliziten Play-Zustand, fehlende/eingeschränkte Geräte, Rückfall, Geräteparameter, deaktivierte Transferautomatik und Volume. Native IPC prüft Persistenz nach Neustart und die neuen Command-Argumente; der React-Test prüft Speicherung und Aktivierung. Lokal bestehen 205 Rust-Tests, 64 Frontend-Tests und TypeScript-/Produktionsbuild.

CI-Stand: Run 37074486729 (`9c3e681`) wurde durch den folgenden Push beendet, nachdem die Tests bestanden waren. Run 37075644434 (`71da6a3`) ist vollständig erfolgreich: Tauri-Tests und Installer-Builds auf Windows/macOS sowie .NET, Architektur, Overlay und Dependency-Audit. Die Windows-/macOS-Paketartefakte sind vorhanden. Erfolgreiches Packaging belegt keine Installation oder Live-Verbindung. Der Gesamtumfang mit O4/O6, verbleibenden OBS-/Twitch-Paketen, MU4/MU6–MU9, AL1/AL2, Dashboard und Systempaketen bleibt verbindlich offen.

## Fortsetzung: C#-Musikprovider importieren

Ein Regressionstest belegte, dass importiertes `MusicPlayer.ProviderId=ytmusic` im Tauri-Overlay und auf der Musikseite fälschlich Spotify auswählte. Eine gemeinsame Rust-Auflösung berücksichtigt jetzt C#-`ProviderId` und eine explizite Tauri-`Source`; Overlay und Alert-Ducking verwenden dieselbe Auflösung. Die Musikseite zeigt den importierten Provider und schreibt bei einer bewussten Änderung beide Felder, damit die Auswahl auch beim Öffnen in WPF erhalten bleibt. Unveränderte importierte Felder bleiben verlustfrei erhalten.

Lokale abschließende Validierung dieses Schritts: 206 Rust-Tests, 65 Frontend-Tests und TypeScript-/Vite-Produktionsbuild. MU9 bleibt für gemeinsame Playerdarstellung und vollständige Anzeigeoptionen teilweise offen. Run 37076605267 für `3b7a255` ist erfolgreich einschließlich Windows-/macOS-Packaging, Geräteverwaltung und Provider-Korrektur. Keine Live-/Installationsabnahme und kein Cutover.

## Fortsetzung: App-Profile (SYS3)

Die Einstellungen enthalten eine Profilverwaltung mit Erstellen, Umbenennen/Beschreibung, Anwenden, Import, Export und Löschen. Rust speichert die PascalCase-Dokumente atomar unter `Profiles/<Id>.json` im bestehenden Datenverzeichnis. Import liest `.ccsprofile` oder JSON, vergibt eine neue ID und ergänzt den Namen um „(Import)“. Unbekannte Einstellungen und Profilmetadaten bleiben erhalten. Das historische StreamerBot-Passwort wird beim Speichern/Exportieren geleert und beim Anwenden aus den aktuellen Einstellungen beibehalten; OS-Keyring-Zugangsdaten werden nicht verändert. Profile enthalten Einstellungen, keine Layout- oder Mediendateien; vollständige Sicherung/Wiederherstellung bleibt SYS4.

Das Anwenden verwendet denselben Backend-Ablauf wie das Speichern von Einstellungen, einschließlich Validierung, Portreservierung, Serveraustausch und Wiederverbindung. Bei zwischenzeitlich geänderten Einstellungen wird abgebrochen. Die Oberfläche lädt die Einstellungen und das Formular anschließend neu, zeigt Verbindungswarnungen und aktualisiert das Theme. Import-/Exportdialoge sind nur für das Hauptfenster freigegeben.

Regressionen prüfen C#-Import, unbekannte Felder, Passwortbehandlung, Neustart, Datei-Ersetzung, defekte/überlange Dokumente, Pfadvalidierung und fehlschlagende Schreibvorgänge. Native Tauri-IPC prüft den vollständigen Profilablauf sowie belegte Ports und einen erfolgreichen Portwechsel mit echter HTTP-Health-Abfrage. UI-Tests prüfen Bedienung, Dialogabbruch, Bestätigung, Fehler/Warnungen und das erneuerte Einstellungsformular. Lokale Validierung: 212 Rust-Tests, 69 Frontend-Tests sowie TypeScript-/Vite-Produktionsbuild; npm-Audit ohne Befunde. Die native Dateidialog- und Dienstabnahme in installierten Windows-/macOS-Paketen bleibt offen.
