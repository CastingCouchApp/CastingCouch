# Streamstart und verbleibende M2-Bedienpultabläufe

Stand: 2026-10-03. Bestätigter Streamstart mit konfigurierter Startszene ist implementiert und automatisiert geprüft. M2 und DA2 sind noch nicht abgeschlossen; die installierte Betriebsabnahme auf Windows/macOS bleibt offen.

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

| Ablauf | Referenz und tatsächlich vorhandenes Verhalten | Verbleibende Arbeit |
|---|---|---|
| Vorprüfung | `Shell/Dashboard/MainWindow.Dashboard.Runtime.cs`, `RunDashboardPreflightAsync`: Dienste, Start-/Liveszene, Titel, Kategorie, Startplaylist, Raid-Ziel; Ergebnisse als Prüfliste und Warnungszahl. | Native Momentaufnahme und erreichbare Dashboard-Karte; gestrichene Streamer.bot-Verbindung auslassen. Prüfung soll Warnungen zeigen und keinen Stream automatisch starten. |
| App-Benachrichtigungen | Dieselbe Datei: `notifications.json`, PascalCase-Felder Timestamp/Severity/Message/IsRead, letzte 250 Einträge, neueste 100 anzeigen, Filter, ungelesene Zahl. `MainWindow.Dashboard.Bindings.cs`: alle gelesen markieren und leeren. | Kompatible native Persistenz, Fehler-/Erfolgseinträge aus tatsächlichen App-Operationen, Events und UI; keine Gleichsetzung mit Twitch-Ereignisfeed. |
| Schnellzugriffe | `Views/Pages/Dashboard/DashboardPageView.xaml` und Dashboard-Bindings: konfigurierte Start-/Live-/Pause-/Endszene, Audiomixer, Raid-Zielprüfung, Profile. | Fehlende direkte Karten/Zugriffe ergänzen; Profile sollen vorhandene Settings-Anwendung verwenden. Workflow-/Vorbereitungsaufrufe bleiben ausgeschlossen. |
| OBS-Monitoring | `Shell/Services/Obs/MainWindow.Services.Obs.ConnectionDashboard.cs`, `RefreshObsProfessionalControlAsync`: Stream-/Aufnahme-Zeitcode, CPU/FPS/RAM, Render-/Encoding-Lag, Replay und Kamera. | Tatsächliche vorhandene Statusfelder anzeigen; unbekannte/fehlgeschlagene Abfragen getrennt behandeln und Wiederverbindung prüfen. |
| OBS-Quellen | `MainWindow.Services.Obs.ServiceSources.cs`: Quellenfilter, Gruppen, Transformation, Filter aktivieren/deaktivieren, Medien restart/stop und Browser ohne Cache neu laden. | Verbleibende Medien-/Browseroperationen und Bedienung abgleichen. Allgemeine quellentypspezifische oder Filterparameter-Editoren wurden in dieser C#-Bedienoberfläche nicht gefunden; vor einer zusätzlichen Paritätsanforderung eine konkrete Referenz verlangen. |
| Alerts | `Shell/Alerts/MainWindow.Alerts.Editor.cs`: Audioausgänge auflisten/auswählen, separater Ausschnitt als lokale Vorschau; Text-/Medienvorschau. `Modules.Alerts/ObsAlertRenderer.cs`: vorhandene Text-/Medienquellen, statisches Layout, Wiedergabe und Cleanup. | Ausgabeauswahl und tatsächliches Verhalten abgleichen. Der OBS-Renderer verwendet weder `SoundPath` noch `Animation`; lokale Vorschau spielt Sound über das WPF-MediaElement, nicht über den gespeicherten Gerätewert. Diese bereits in C# unvollständigen Funktionen gesondert dokumentieren und nicht als belegte OBS-Wiedergabe behandeln. |

Maßgeblich bleiben der [Umsetzungsplan](TAURI-IMPLEMENTATION-PLAN.md) und die [Feature-Matrix](TAURI-FEATURE-PARITY.md). Die Referenzprüfung ersetzt keine Umsetzung oder Betriebsabnahme weiterer M2-Pakete.
