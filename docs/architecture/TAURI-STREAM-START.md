# Streamstart und verbleibende M2-Bedienpultabläufe

Stand: 2026-10-03. Bestätigter Streamstart mit konfigurierter Startszene und native Vorprüfung sind implementiert und automatisiert geprüft. M2 und DA2 sind noch nicht abgeschlossen; die installierte Betriebsabnahme auf Windows/macOS bleibt offen.

## Streamstart

Referenz: `src/CreatorControlSuite.App/Shell/Services/Obs/MainWindow.Services.Obs.StreamStart.cs`, Methode `StartObsStreamAsync`.

- Der gemeinsame OBS-Bereich in Dashboard und Dienste fragt „OBS-Stream wirklich starten?“. Ablehnung sendet keinen Startbefehl.
- Der native `obs_control`-Command lädt die gespeicherte Startszene unter derselben Settings-/Streamende-Sperre wie die übrigen Abläufe. Ein aktiver Streamende-Assistent verhindert einen neuen App-Start.
- `ObsClient::start_stream_with_scene` liest zuerst `GetStreamStatus`. Ein bereits laufender oder unbekannter Streamstatus löst weder Szenenwechsel noch Start aus. Bei konfigurierter Startszene muss deren Wechsel erfolgreich sein, bevor `StartStream` gesendet wird. Ohne Startszene bleibt der bisherige Szenenname erhalten.
- Ein abgelehnter Start bleibt ein sichtbarer Fehler. Es gibt keinen automatischen Wiederholungsversuch einer möglicherweise bereits angenommenen Startmutation und keinen vorzeitigen App-Livezustand.
- Sitzungserfassung und Startmusik bleiben an den tatsächlichen OBS-Ereignissen beziehungsweise bestätigten Ausgangsstatus. Vorhandene Szenenregeln bleiben eigenständig. Die native Startfunktion ruft keine Startplaylist direkt auf. Der vorhandene Dashboard-Fokus folgt weiterhin dem tatsächlichen Livezustand und den gespeicherten Fokusoptionen.
- Der C#-Aufruf des Workflow-Startcountdowns wird nicht übernommen, weil das Workflow-Modul gestrichen ist. Der unabhängige globale Overlay-Countdown bleibt bedienbar. Der bereits in C# deaktivierte automatische Wechsel zur Liveszene wird nicht als zusätzliche Anforderung eingeführt.

## Prüfungen

Vor der Korrektur fehlgeschlagene UI- und native IPC-Tests belegen die fehlende Bestätigung und den ungeprüften Start. Die UI-Regression prüft Ablehnung, bestätigten Start und sichtbaren Szenenfehler ohne behaupteten Livezustand. Eine weitere Regression prüft unvollständige OBS-Statusantworten: Stream bleibt unbekannt und seine Mutationen deaktiviert; verfügbare Aufnahmebefehle bleiben bedienbar.

Der native IPC-Test verwendet den produktiven Command und einen tatsächlichen OBS-v5-WebSocket-Testserver. Er prüft unbekannten/aktiven Ausgang, abgelehnten Szenenwechsel, abgelehnten Start, Reihenfolge `GetStreamStatus → SetCurrentProgramScene → StartStream`, genauen Szenennamen und Sitzungserfassung erst nach dem echten Testserver-Ereignis. Der durchgehende Raid-Test beginnt jetzt ebenfalls mit einem offline gemeldeten OBS-Ausgang und tatsächlichem Start vor der Prüfung ausgehender Raid-Subscriptions.

Geprüft: vollständiger Rust-Workspace, 178 Frontend-Tests, generierter Command-Vertrag, TypeScript und Produktionsbuild. Tests mit lokalen Gegenstellen sind kein Nachweis eines realen Streams oder installierten Pakets.

## Konkrete weitere C#-Referenzen für M2

### Vorprüfung: implementierter Ablauf

Die Karte `Preflight` ist im nativen Dashboard-Katalog und der produktiven Route enthalten; bestehende Reihenfolge, ausgeblendete Karten und die Gruppe `ShowAdvancedTools` bleiben wirksam. „Vorprüfung ausführen“ fragt den typisierten Command `dashboard_preflight` ab. Die Prüfung verändert weder Einstellungen noch OBS-/Musikwiedergabe und ist unabhängig von der Startbestätigung.

Die neun Prüfungen übernehmen OBS-Verbindung, Twitch-Verbindung, Verbindung des gewählten Musikproviders, konfigurierte Start-/Liveszene, aktuellen Twitch-Titel/Kategorie, erforderliche Spotify-Startplaylist und erforderliches Raid-Ziel. Streamer.bot ist entsprechend dem gestrichenen Integrationsumfang ausgelassen. YouTube Music wird über den tatsächlichen Bridge-Zustand geprüft; seine Auswahl erfordert keine Spotify-Startplaylist. Explizite native Musikflags haben Vorrang vor erhaltenen historischen Workflow-Flags.

Abschlussprüfung dieses Abschnitts: vollständiger Rust-Workspace, 179 Frontend-Tests in 45 Dateien, Command-Vertrag, TypeScript und Produktionsbuild erfolgreich. Die installierte Windows-/macOS-Abnahme bleibt offen.

Anders als ein ungespeicherter C#-Textentwurf verwendet die native Prüfung frisch abgefragte Twitch-Kanalinformationen. HTTP-Fehler werden bei Titel/Kategorie als nicht bestandene Prüfung angezeigt; vorherige erfolgreiche Angaben gelten dabei nicht als aktueller Nachweis. Jede Prüfung zeigt Zeitpunkt, Details und Warnungszahl. Scheitert die gesamte Abfrage, bleibt der Fehler sichtbar und ein vorhandenes Ergebnis ist ausdrücklich als vorherige Prüfung markiert.

UI- und native IPC-Tests wurden vor der Implementierung hinzugefügt. Native Tests verbinden den tatsächlichen Tauri-Command mit lokaler Helix-HTTP einschließlich 403 nach vorherigem Erfolg und prüfen Offline-Verhalten, aktuelle Settings, unveränderte Persistenz und fehlenden Streamstart. Modultests prüfen alte/native Musikflags, Providerwahl und unbekannte Kanalinformationen. UI-Tests prüfen manuelles Auslösen, Prüfliste, Fehler/Wiederholung und die produktive Dashboard-Einbindung. Die Browser-Darstellung wurde zusätzlich mit einer getrennten UI-Testfixture und simulierten Diensten geprüft. Start, Abschluss und Fehler sind jetzt an das native [App-Benachrichtigungsjournal](TAURI-NOTIFICATIONS.md) angebunden; dessen Speicherfehler verhindern die Prüfung nicht.

| Ablauf | Referenz und tatsächlich vorhandenes Verhalten | Verbleibende Arbeit |
|---|---|---|
| Vorprüfung | `Shell/Dashboard/MainWindow.Dashboard.Runtime.cs`, `RunDashboardPreflightAsync`: Dienste, Start-/Liveszene, Titel, Kategorie, Startplaylist, Raid-Ziel; Ergebnisse als Prüfliste und Warnungszahl. | Native Momentaufnahme, Dashboard-Karte und Journaleinträge sind implementiert. Installierte Betriebsabnahme bleibt offen. |
| App-Benachrichtigungen | Dieselbe Datei: `notifications.json`, PascalCase-Felder Timestamp/Severity/Message/IsRead, letzte 250 Einträge, neueste 100 anzeigen, Filter, ungelesene Zahl. `MainWindow.Dashboard.Bindings.cs`: alle gelesen markieren und leeren. | [Persistenz, UI und native Stream-/Dienstereignisse](TAURI-NOTIFICATIONS.md) sind implementiert und geprüft. Weitere Meldestellen bei Schnellzugriffen, Musik, Moderation und Diagnostik sowie installierte Betriebsabnahme bleiben offen. |
| Schnellzugriffe | `Views/Pages/Dashboard/DashboardPageView.xaml` und Dashboard-Bindings: konfigurierte Start-/Live-/Pause-/Endszene, Audiomixer, Raid-Zielprüfung, Profile. | Diese [drei Karten samt Schnellprofil](TAURI-DASHBOARD-SHORTCUTS.md) sind eingebunden und geprüft; Profile verwenden dieselbe native Settings-Anwendung. [Dienst-Schnellstarts/-öffnen](TAURI-DASHBOARD-SERVICES.md) sind ebenfalls implementiert; Musikaktionen und weitere Meldestellen bleiben offen. Workflow-/Vorbereitungsaufrufe bleiben ausgeschlossen. |
| OBS-Monitoring | `Shell/Services/Obs/MainWindow.Services.Obs.ConnectionDashboard.cs`, `RefreshObsProfessionalControlAsync`: Stream-/Aufnahme-Zeitcode, CPU/FPS/RAM, Render-/Encoding-Lag, Replay und Kamera. | [Monitoringfelder, unabhängige Fehlerzustände, Refresh und Wiederherstellung](TAURI-OBS-MEDIA-MONITORING.md) implementiert und geprüft; echte/installierte Abnahme offen. |
| OBS-Quellen | `MainWindow.Services.Obs.ServiceSources.cs`: Quellenfilter, Gruppen, Transformation, Filter aktivieren/deaktivieren, Medien restart/stop und Browser ohne Cache neu laden. | [Medien-/Browseroperationen](TAURI-OBS-MEDIA-MONITORING.md) sind implementiert und geprüft. Szenen-/Quellen-/Eingangs-Suchfilter noch übernehmen. Allgemeine quellentypspezifische oder Filterparameter-Editoren wurden in dieser C#-Bedienoberfläche nicht gefunden; vor einer zusätzlichen Paritätsanforderung eine konkrete Referenz verlangen. |
| Alerts | `Shell/Alerts/MainWindow.Alerts.Editor.cs`: Audioausgänge auflisten/auswählen, separater Ausschnitt als lokale Vorschau; Text-/Medienvorschau. `Modules.Alerts/ObsAlertRenderer.cs`: vorhandene Text-/Medienquellen, statisches Layout, Wiedergabe und Cleanup. | Ausgabeauswahl und tatsächliches Verhalten abgleichen. Der OBS-Renderer verwendet weder `SoundPath` noch `Animation`; lokale Vorschau spielt Sound über das WPF-MediaElement, nicht über den gespeicherten Gerätewert. Diese bereits in C# unvollständigen Funktionen gesondert dokumentieren und nicht als belegte OBS-Wiedergabe behandeln. |

Maßgeblich bleiben der [Umsetzungsplan](TAURI-IMPLEMENTATION-PLAN.md) und die [Feature-Matrix](TAURI-FEATURE-PARITY.md). Die Referenzprüfung ersetzt keine Umsetzung oder Betriebsabnahme weiterer M2-Pakete.
