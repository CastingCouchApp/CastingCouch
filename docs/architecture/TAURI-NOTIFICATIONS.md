# App-Benachrichtigungen in Tauri

Stand: 2026-10-03. Journal, Dashboard-Karte und die unten aufgeführten Ereignisquellen sind implementiert und automatisiert geprüft. DA2 und die installierte Windows-/macOS-Betriebsabnahme bleiben offen.

## C#-Vertrag und Daten

Referenz: `src/CreatorControlSuite.App/Shell/Dashboard/MainWindow.Dashboard.Runtime.cs`, Methoden `AddDashboardNotification`, `RefreshDashboardNotificationView`, `LoadDashboardNotificationsAsync`, `SaveDashboardNotificationsAsync`; Bedienung in `MainWindow.Dashboard.Bindings.cs`. Die Karte heißt in den gespeicherten Dashboard-Einstellungen `Notifications`, ihre Standardspalte ist gemäß `Core/Configuration/DashboardSettings.cs` rechts.

Die Datei liegt direkt unter `AppPaths.data_root/notifications.json`, unter Windows im bestehenden lokalen `CreatorControlSuite`-Datenverzeichnis. Das Format bleibt ein JSON-Array mit `Timestamp`, `Severity`, `Message` und `IsRead`. Unbekannte Zusatzfelder und die originale Zeitstempelrepräsentation einschließlich Zeitzone bleiben beim Lesenmarkieren erhalten. UTF-8-BOM und der C#-Leerzustand `null` werden unterstützt. Zeitstempel werden vor der Übernahme geprüft; Dateien über 16 MiB werden zur manuellen Prüfung erhalten.

Es bleiben die letzten 250 Einträge nach Einfügereihenfolge gespeichert. Je Filter erscheinen höchstens die neuesten 100 nach tatsächlichem Zeitpunkt. Filter: Alle, Info, Warnungen, Fehler. Der Ungelesen-Zähler zählt alle gespeicherten Einträge. Neue Schweregrade `Error`/`Fehler` und `Warning`/`Warnung` werden wie in C# normalisiert; andere neue Werte ergeben Info.

## Speicherung und Fehler

`ccs-modules::notifications::NotificationRuntime` ist der gemeinsame native Zustand. Ein Mutex serialisiert Produzenten und Bearbeitung. Eine temporäre Datei wird vollständig geschrieben und synchronisiert, bevor sie die Journaldatei ersetzt; ein fehlgeschlagener Schreibvorgang lässt die vorhandene Datei bestehen.

Neue Laufzeitmeldungen bleiben bei Speicherfehlern im Arbeitsspeicher sichtbar. Dieser Fehler verhindert weder eine Vorprüfung noch den eigentlichen Streamablauf. Snapshot und UI zeigen ausdrücklich, dass die Speicherung nicht abgeschlossen ist. Lesenmarkieren und Leeren verändern den sichtbaren Zustand erst nach erfolgreicher Speicherung. Fehlgeschlagene Änderungen bleiben Fehler und können wiederholt werden.

Eine nicht lesbare oder beschädigte vorhandene Datei blockiert den App-Start nicht und wird nicht durch neue Ereignisse überschrieben. „Speichern erneut versuchen“ liest sie nochmals: eine zwischenzeitlich reparierte Datei wird mit den neuen Meldungen zusammengeführt; eine weiterhin beschädigte, lesbare Datei wird vor dem Ersetzen vollständig als `notifications-corrupt-<UUID>.json` gesichert. Der Sicherungspfad bleibt im laufenden Snapshot sichtbar. Nicht lesbare und übergroße Dateien benötigen eine manuelle Reparatur; ein Retry behauptet dafür keinen Erfolg.

## Commands und Ereignisse

| Command | Eingabe | Wirkung |
|---|---|---|
| `notifications_snapshot` | `filter` | Typisierter Snapshot: Einträge, Gesamtzahl, Ungelesen-Zahl, Warnungen, Reparatursicherung |
| `notifications_mark_read` | — | Alle Einträge als gelesen speichern |
| `notifications_clear` | — | Journal leeren und speichern; UI fragt vorher nach |
| `notifications_retry` | — | Laden/Reparieren/Speichern erneut versuchen |

Produktiver Host und native IPC-Test-App registrieren dieselben Commands. Dateiarbeit der Commands läuft in `spawn_blocking`. Der generierte TypeScript-Vertrag enthält `NotificationItem` und `NotificationSnapshot`. `notifications-changed` invalidiert die Dashboard-Abfrage; Fallback-Polling holt verpasste Aktualisierungen nach. Listener werden beim Unmount entfernt, Registrierungsfehler sind sichtbar und erneut verbindbar.

Die konfigurierbare Karte `Notifications` verwendet `ShowNotifications`, vorhandene Reihenfolge, Spalte, Größe und ausgeblendete Karten. Im Fokusmodus bleibt sie entsprechend C# ausgeblendet. Der Twitch-Ereignisfeed besteht separat weiter.

## Angebundene tatsächliche Ereignisquellen

- OBS-Stream gestartet/beendet: bestätigte Ausgangsereignisse, keine bloße Start-Anfrage. Doppelte und zeitlich ältere Ereignisse erzeugen keine zusätzlichen Meldungen.
- Abgelehnter App-Streamstart: ursprünglicher Command-Fehler bleibt erhalten und erhält einen Fehlereintrag.
- Vorprüfung: Start, Abschluss mit Warnungszahl oder Fehler der gesamten Abfrage. Ein Journal-Speicherfehler verändert das Prüfergebnis nicht.
- OBS/Twitch/Spotify-Verbindungsstatus: Verbindungen, Trennung und Fehler aus den nativen Statuskanälen; wiederholte identische Meldungen und Connecting-Polls werden unterdrückt.
- Streamende-Assistent: Phasenwechsel, Fehler und einzelne Warnungen aus derselben Runtime wie Dashboard und Stoppdialog. Countdown-Ticks erzeugen keinen Meldungsspam. Nach überlaufener Event-Queue wird der aktuelle Runtime-Zustand übernommen.
- [Szenen-/Audio-Schnellzugriffe und Profilanwendung](TAURI-DASHBOARD-SHORTCUTS.md): gemeinsame native Commands protokollieren tatsächliche Ergebnisse und Fehler; Profilwarnungen bleiben sichtbar.
- [Dienst-Programmstart](TAURI-DASHBOARD-SERVICES.md): gespeicherte OBS-/Spotify-Pfade, bereits laufendes Programm, angenommener Start oder ursprünglicher Fehler. Ein Programmstart wird nicht als Dienstverbindung protokolliert.
- [Gemeinsame Musikaktionen und Spotify-Schnellzugriffe](TAURI-DASHBOARD-MUSIC.md): API-Annahme beziehungsweise Bridge-Queue als „angefordert“, ursprüngliche Ablehnung und expliziter Teilerfolg bei Playliststart mit fehlgeschlagener Verlaufsspeicherung. Kein Erfolgseintrag für einen abgelehnten Command.

Weitere C#-Meldestellen in Musikautomationen, Moderation und Diagnostik werden beim Abschluss dieser Abläufe abgeglichen. Eine vollständige Übernahme aller C#-Meldestellen wird mit diesem Abschnitt nicht behauptet. Gestrichene Workflow-/externe Funktionen werden nicht als Meldungsquellen wieder eingeführt.

## Nachweise und verbleibende Abnahme

Die zuerst fehlgeschlagenen Tests belegen fehlendes Modul, fehlende native Command-Registrierung und fehlenden Dashboard-Katalogeintrag. Sechs Modultests prüfen Legacy-Datei/250er-Limit/100er-Filter, Zusatzfelder, Lesestatus/Neustart/Leeren, beschädigte Daten mit Sicherung, extern reparierte Datei mit BOM, Schreibfehler, konkurrierende Produzenten und deduplizierte Stream-/Dienst-/Assistentenereignisse. Dashboard-Modultests sichern Legacy-Key und Layout-Roundtrip ab.

Zwei native IPC-Tests verbinden die Commands mit tatsächlicher Dateipersistenz und Ereigniszustellung. Sie prüfen Fehler und Retry, native Statusweiterleitung, Vorprüfungsfehler und Streamende-Warnungen. Der bestehende OBS-v5-WebSocket-/IPC-Test prüft zusätzlich: vier abgelehnte Starts ergeben ausschließlich Fehler; erst die tatsächliche `StreamStateChanged`-Bestätigung ergibt einen Starteintrag.

Vier UI-Tests prüfen Filter, Mutationen, bestätigtes Leeren, erfolgreiche/fehlgeschlagene Speicherung, Ereignisaktualisierung, Listener-Cleanup und Fokus-/Gruppensichtbarkeit. Der produktive Dashboard-Routentest belegt die montierte Karte. Eine getrennte Browser-Testfixture belegt Darstellung, sichtbaren Schreibfehler, Wiederholung, Lesestatus, Fehlerfilter und Aktualisierung durch ein simuliertes Ereignis. Sie verwendet keine realen Dienste.

Vollständiger Rust-Workspace, 183 Frontend-Tests in 46 Dateien, Command-Vertrag, TypeScript und Produktionsbuild sind erfolgreich. Praktische Wiederaufnahme mit realen Diensten und Journal nach Neustart eines installierten Windows-/macOS-Pakets bleibt offen. Weitere Arbeit: [M2-Plan](TAURI-IMPLEMENTATION-PLAN.md).
