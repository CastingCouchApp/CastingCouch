# Anpassbares Tauri-Dashboard

Stand: 2026-10-03. DA1 ist implementiert; die installierte Betriebsabnahme auf Windows/macOS bleibt offen. DA2 verwendet die vorhandenen Live-Funktionen gemeinsam mit den Serviceseiten einschließlich Streamende-/Raid-Assistent. Weitere C#-Bedienpultabläufe bleiben offen. WPF bleibt verfügbar.

## C#-Referenz und übernommenes Verhalten

Maßgeblich sind `Core/Configuration/DashboardSettings.cs`, `App/Shell/Dashboard/MainWindow.Dashboard.SceneButtons.cs`, `MainWindow.Dashboard.Layout.cs`, `MainWindow.Dashboard.Ordering.cs`, `App/Views/Dialogs/DashboardSceneButtonEditorWindow.xaml.cs` sowie die Kennzahlauswahl in `MainWindow.Services.Twitch.ApiProfessional.cs`.

Das Dashboard bietet Sichtbarkeit und Reihenfolge pro Karte, die Spalten Links/Mitte/Rechts, Größen Kompakt/Standard/Groß, Gruppenfilter, Layoutvorgaben und einen temporären Fokusmodus. Unterhalb des Desktop-Breakpoints erscheinen die Karten in der gespeicherten Gesamtreihenfolge. Auf breiten Fenstern gilt diese Reihenfolge innerhalb der gewählten Spalte. Die Größen begrenzen den scrollbaren Kartenbereich auf 384/704 Pixel beziehungsweise lassen ihn unbegrenzt. Die alten `ModuleWidths`/`ModuleHeights` bleiben erhalten; auch der aktuelle responsive C#-Code wendet diese gespeicherten festen Pixelgrößen nicht auf seine Slots an.

Die Gruppenstatuswerte werden übernommen. Ein als sichtbar markiertes Modul bleibt ausgeblendet, solange seine Gruppe deaktiviert ist; der Editor zeigt diesen Zustand ausdrücklich. Fokusmodus aktiviert vorübergehend Verbindungen, Streamsteuerung, Live-Panels und Auswertungen und blendet Ereignisse und Verlauf aus. Er verändert keine gespeicherten Einstellungen. Automatische Aktivierung/Beendigung verwendet bestätigten OBS-Streamstatus. Eine fehlende Verbindung oder unbekannter Status wird nicht als Streamende ausgelegt.

Szenenbuttons unterstützen stabile IDs, eigenen Namen, frei wählbare OBS-Szene, Emoji, die bisherigen MDL2-Glyphcodes, lokale Rasterbilder und Bilder der bestehenden Asset-Bibliothek. Die zwölf angebotenen Windows-Glyphcodes besitzen eine plattformunabhängige Darstellung; ihre gespeicherten Werte bleiben kompatibel. Eine optionale `Color` ergänzt die gewünschte individuelle Buttonfarbe; ohne Farbe wird das Theme verwendet. Der aktive Button wird anhand der tatsächlichen OBS-Szene hervorgehoben.

Ohne vorhandene Buttons werden die konfigurierten Start-/Live-/Pause-/End-Szenen mit den bisherigen Emojis angeboten. IDs bleiben dabei stabil und gleiche Szenennamen werden ohne Beachtung der Groß-/Kleinschreibung zusammengefasst, auch bei Umlauten. Nach bewusstem Löschen aller Buttons verhindert `SceneButtonsInitialized` deren automatische Wiederanlage. OBS-Szenenliste, gespeicherte Auswahl und zusätzliche konfigurierte Szenen stehen als Vorschläge bereit; freie Eingabe bleibt möglich.

## Fachlogik und Persistenz

`ccs-modules/src/dashboard.rs` liest die kompatiblen PascalCase-Einstellungen und stellt einen typisierten camelCase-Entwurf bereit. Die bestehenden Settings bleiben die einzige Persistenzquelle. `StreamStatistics` wird für die Darstellung als `Community` behandelt. Ausgeschlossene oder noch nicht angebundene Module bleiben als Daten erhalten, werden aber nicht als funktionsfähige Karten angeboten. Workflow, externe Steuerung/Multi-PC und kommerzielle Lizenzierung werden nicht wieder eingeführt.

Der Adapter schreibt ausschließlich tatsächlich bearbeitete Felder zurück. Unbekannte Dashboard-Felder, Dictionary-Einträge, Module und Zusatzfelder einzelner Szenenbuttons bleiben erhalten. Auch unberührte Feldrepräsentationen und unbekannte Symboltypen werden nicht bei einer Namensänderung ersetzt. Nicht lesbare Listen/Objekte oder beschädigte bekannte Button-Felder verhindern das Bearbeiten statt eine scheinbar erfolgreiche Rücksetzung auszulösen.

Das Speichern verwendet den gemeinsamen Settings-Mutex und `JsonSettingsStore::save_edit` mit dem ursprünglichen Snapshot. Unabhängige Änderungen anderer Seiten werden zusammengeführt; konkurrierende Änderungen derselben Werte melden einen Konflikt. Fehler behalten den Entwurf. Ereignisse und Fallback-Polling aktualisieren die gespeicherte Ansicht, überschreiben aber keine geöffneten Entwürfe. Eine fehlgeschlagene Ereignisregistrierung ist sichtbar und erneut verbindbar. Fehler nach erfolgreichem Speichern beim Emitten werden als Warnung gemeldet.

Neue/geänderte Bildpfade werden vor dem Speichern geprüft. PNG/JPEG/GIF/WebP/BMP werden lokal, mit Formatkennung und maximal 15 MiB, als Data-URL bereitgestellt. Fehlende Dateien und Darstellungsfehler besitzen einen sichtbaren Fallback. Die Bibliothek verwendet den vorhandenen Index und tatsächliche Asset-Dateien; SVG wird wegen des bisherigen Rasterbild-Vertrags nicht als Szenenbild angeboten. Dateiarbeit und Validierung laufen außerhalb des asynchronen Dienst-Executors.

## Native Commands und Ereignisse

| Command | Eingabe | Ergebnis |
|---|---|---|
| `dashboard_snapshot` | — | Vollständiges Original, Entwurf, Szenenvorschläge, Warnungen |
| `save_dashboard` | `original`, typisierter `draft` | Gespeicherter Snapshot; `dashboard-changed` |
| `dashboard_image_preview` | `path` | Lokale Rasterbild-Data-URL oder Fehler |
| `dashboard_asset_choices` | — | Bibliotheksbilder mit tatsächlichen lokalen Pfaden |
| `dashboard_obs_preview` | — | PNG-Data-URL und tatsächliche OBS-Canvas-Dimensionen |

Die Commands sind im produktiven Host und in der nativen IPC-Test-App registriert. Der generierte TypeScript-Command-Vertrag umfasst die vollständigen Dashboard-, Karten-, Button- und Präferenzentwürfe.

## Gemeinsame Live-Panels

- Die Karte `Preflight` führt eine native [Vorprüfung](TAURI-STREAM-START.md) mit neun Einzelpunkten und aktuellem Twitch-Kanalabruf aus. Sie ist der Gruppe `ShowAdvancedTools` zugeordnet, beginnt keine Wiedergabe und zeigt fehlgeschlagene Punkte beziehungsweise Abfragefehler ausdrücklich an.

- Vorhandene OBS-Ausgänge und globaler Overlay-Countdown bleiben bedienbar. Die OBS-Vorschau fragt Videoeinstellungen und Screenshot über denselben OBS-Client ab; kompakt/standard/groß begrenzt die Bildbreite auf 200/400/800 Pixel bei tatsächlichem Seitenverhältnis.
- `TwitchChat` wird von Dashboard und Serviceseite verwendet: App-Chatfeed, Fragments/Emotes/Badges, Senden, Webchat öffnen, Nachrichten löschen, Timeout und Moderationsauswahl. Empfang und Moderationsbereinigung bleiben über den vorhandenen Ereignisfluss synchronisiert. Ein während des Sendens neu bearbeiteter Nachrichtenentwurf wird nach Erfolg der vorherigen Nachricht nicht gelöscht.
- `TwitchEventFeed` nutzt den vorhandenen unabhängigen App-Ereignisfeed.
- Die Karte `Notifications` enthält das kompatible [App-Benachrichtigungsjournal](TAURI-NOTIFICATIONS.md), unabhängig vom Twitch-Feed: Filter, Ungelesen-Zahl, alle gelesen markieren, bestätigtes Leeren, sichtbare Speicherfehler und Retry. Bestätigte OBS-Ausgänge, Startfehler, Vorprüfung, Dienstverbindungen und Streamende-Runtime sind angebunden. Bestehende `ShowNotifications`-/Layoutwerte und Fokusmodus bleiben wirksam.
- Der gemeinsame Musikplayer verwendet den gespeicherten Provider sowie dessen tatsächliche Fähigkeiten und Metadaten. Es entsteht keine zweite Wiedergabelogik im Dashboard.
- Community zeigt Zuschauer, Follower, Subscriptions und Chatter sowie die gewählte Hauptkennzahl. Die C#-Auswahl Neue Follower/Neue Subscriptions verwendet die aktive aufgezeichnete Sitzung. Fehlende Werte werden als unbekannt angezeigt; erhaltene Werte nach Abbruch oder API-Fehler als veraltet. Der Zuschauerverlauf nutzt bis zu 48 tatsächlich aufgezeichnete Samples.
- Streamhistorie und Creator Intelligence bleiben eigene konfigurierbare Karten mit den bereits portierten Persistenz- und Auswertungsfunktionen.

Die Browser-Demo ist ausdrücklich gekennzeichnet. Ihr Dashboard-Entwurf wird durch einen Rust-Test gegen den nativen Standardvertrag geprüft. Sie belegt weder Dienstverbindungen noch erfolgreiches Speichern.

## Validierung und verbleibende Abnahme

Tests wurden vor den neuen Implementierungen beziehungsweise konkreten Fehlerkorrekturen hinzugefügt. Sieben Rust-Modultests decken C#-Layoutalias, verlustfreie Änderungen, Default-IDs, bewusst leere Buttons, Unicode, beschädigte Daten, Bildgrenzen und den gemeinsamen Standardvertrag ab. Der native IPC-Test prüft tatsächliche Settings-Dateien, parallele Änderungen, Konflikte, Ereigniszustellung, Bibliothek, Bildbereitstellung und Wiederherstellung nach Neustart. Der vorhandene OBS-IPC-/WebSocket-Test prüft zusätzlich die Screenshot-Anfrage mit aktivem Szenennamen, Dimensionen und fehlender Verbindung.

Dashboard-UI-Tests prüfen Sichtbarkeit, Größen/Spalten, Reihenfolge, Presets, Fokuswechsel, erhaltene Entwürfe, Fehler, Szenenwechsel, Kennzahlen-Ereignisse und Preview. Nicht unterstützte oder beschädigte Zuschauer-Samples aus alten Daten verhindern nicht die Bedienung und werden nicht als Graphpunkte verwendet. Die bisherigen Serviceseiten-Chat-Tests gelten auch nach der Extraktion weiter; zusätzliche Tests sichern Nachrichten während laufender/fehlgeschlagener Übertragung ab. Die Route montiert die gemeinsamen Live-Komponenten nachweislich.

Geprüft: vollständiger Rust-Workspace, vollständige Frontend-Suite mit 166 Tests plus zusätzlicher Regressionstest für alte Samples, Command-Vertrag, TypeScript und Produktionsbuild. Browser-Darstellung von Dashboard und Editor bei Standardgröße sowie Kartenreihenfolge bei 900 Pixel Fensterbreite geprüft. Diese Browserprüfung verwendet die Demo. Tatsächliche OBS/Twitch/Spotify/YouTube-Music-Verbindungen und installierte Windows-/macOS-Nutzerabläufe bleiben gesondert nachzuweisen.

Die native [Streamende-/Raid-Steuerung](TAURI-STREAM-END.md) ist als Karte `StreamEnd` und OBS-Stoppdialog eingebunden. Kartenreihenfolge, ausgeblendete Karten und `StreamEndExpanded` bleiben kompatibel; auch das OBS-Fokuslayout enthält die Karte. Beide Ansichten verwenden denselben Runtime-Status und dieselben Settings. Der OBS-Stoppbutton öffnet den Assistenten ohne unmittelbaren Stoppbefehl. Native Raid-/Kanalwechsel-/Shutdown-Verträge, UI-Integration, vollständige Regression mit 176 Frontend-Tests und Produktionsbuild sind geprüft; die Browserprüfung des Dialogs verwendet eine getrennte UI-Testfixture. Damit ist [M1 implementiert](TAURI-IMPLEMENTATION-PLAN.md); die Live-/Installationsabnahme bleibt offen. Streamstart, Vorprüfung und App-Journal sind inzwischen implementiert; der Journalabschnitt besteht den vollständigen Rust-Workspace, 183 Frontend-Tests und Produktionsbuild. DA2 benötigt weiterhin Schnellzugriffe und zugehörige zusätzliche Meldestellen. Die übrigen Pakete des Auswahlplans bleiben verbindlich; die aktuelle Umsetzung erklärt keinen vollständigen Cutover.
