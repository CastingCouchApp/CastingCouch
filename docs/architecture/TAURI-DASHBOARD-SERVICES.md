# Dashboard: Dienste-Schnellzugriff

Stand: 2026-10-03. Implementiert und automatisiert geprüft; echte Dienste und installierte Windows-/macOS-Abnahme bleiben offen.

## C#-Referenz und Umfang

`Shell/Dashboard/MainWindow.Dashboard.Bindings.cs`, `InitializeDashboardBindings`, verbindet Programmstart für OBS/Spotify, Dienstverbindungen, Twitch-Serviceseite, Musikplayer, Test-Alert und Overlay-Seite. `Shell/Lifecycle/MainWindow.Shell.Helpers.cs`, `LaunchConfiguredExecutable`, verwendet den gespeicherten Programmpfad, startet mit dem Programmverzeichnis und überspringt bereits laufende Programme. `Shell/Music/MainWindow.Music.Runtime.cs`, `ApplyMusicProviderUiState`, zeigt den gewählten Musikprovider und blendet den Spotify-Programmstart bei YouTube Music aus.

Tauri übernimmt diese Bedienung als konfigurierbare Karte `QuickServices`. Historische Reihenfolge, Spalte, Größe, ausgeblendete Karte und `ShowQuickServices` bleiben erhalten. Im Fokusmodus wird die Karte ausgeblendet; die Gruppe ist im Dashboard-Editor separat schaltbar. Streamer.bot, Stream Deck, externe Steuerung und Workflow-Vorbereitung gehören nicht zum gewählten Umfang.

## Programmstart

`launch_service(service)` akzeptiert ausschließlich `obs` und `spotify`. Der native Host lädt die aktuellen gespeicherten Einstellungen; der Aufrufer übermittelt weder einen Pfad noch Shell-Argumente. OBS verwendet `Obs.ExecutablePath`, Spotify das kompatible Zusatzfeld `Spotify.ExecutablePath`. Beide Pfade sind in den Einstellungen bearbeitbar; das optionale Spotify-Feld wird bei einer unbeteiligten Änderung alter Daten nicht zusätzlich eingeführt. Unbekannte Musikfelder bleiben erhalten.

`ccs-core::service_launcher` führt Validierung, Prozessprüfung und Start aus. Fehlende, relative oder nicht verfügbare Pfade bleiben Fehler. Windows erwartet eine `.exe`; macOS unterstützt ausführbare Dateien und `.app`-Verzeichnisse mit `Contents/Info.plist`. Dateien werden direkt mit dem Programmverzeichnis als Arbeitsverzeichnis gestartet, ohne Shell und zusätzliche Argumente. App-Bundles werden über `/usr/bin/open -a <Pfad>` geöffnet; ein fehlgeschlagener `open`-Aufruf bleibt ein Fehler. Pfade mit Leerzeichen und Umlauten bleiben einzelne OS-Argumente.

Eine gemeinsame Sperre serialisiert Prozessprüfung und Start. Bei ausführbaren Dateien wird wie in C# der Prozessname verglichen; bei macOS-Bundles der ausführbare Prozess innerhalb des Bundles. Die Prozessabfrage nutzt [sysinfo](https://docs.rs/sysinfo/0.39.6/sysinfo/struct.System.html) mit Namen und ausführbaren Pfaden, ohne Kommandozeilen oder Umgebungsdaten abzufragen. Bereits laufende Anwendungen ergeben `already_running`; angenommene Starts `started`. Ein Startresultat belegt keine erfolgreiche OBS-/OAuth-/Bridge-Verbindung. Die UI sagt ausdrücklich, dass der Verbindungsstatus separat geprüft wird. Ergebnisse und Fehler erreichen das gemeinsame App-Journal.

## Gemeinsame Dienste und Navigation

| Bedienung | Vorhandener Vertrag |
|---|---|
| OBS verbinden/trennen | `connect_obs` / `disconnect_obs` |
| Twitch anmelden/abmelden | `twitch_login` / `twitch_logout`; Öffnen führt zu `/services#twitch` mit tatsächlichem Zielabschnitt |
| Musik verbinden/trennen | `music_player_connect` / `music_player_disconnect`, gespeicherter Provider; Spotify-Start erscheint nur für Spotify |
| Musik öffnen | Bestehende Musikseite `/music` |
| Test-Alert | Aktivierte Definition auswählen, `test_alert(alertType, user)` mit denselben nativen Engine-/Queue-Regeln wie die Alertseite |
| Alerts / Overlay öffnen | Bestehende Seiten `/alerts` und `/overlay` |

Laufende Mutationen und bereits laufende Verbindungsversuche sperren die betreffende Aktion. Fehler bleiben sichtbar und die Aktion ist erneut ausführbar. Nach dem Ergebnis werden Dienst-, Musik- und Alert-Laufzeitabfragen invalidiert; es entsteht keine zweite Verbindung oder Wiedergabelogik.

Ein Alert-Ergebnis `0` bedeutet, dass Engine oder Definition deaktiviert ist; die UI meldet dann ausdrücklich keinen erfolgreichen Test. Ein positives Ergebnis bestätigt ausschließlich die Aufnahme in die Queue. Fehlgeschlagene Definitionsabfragen deaktivieren den Test, auch wenn alte Daten im Cache existieren. Die eigentliche Wiedergabe und deren Fehler bleiben Aufgabe der gemeinsamen Alert-Runtime.

## Nachweise und verbleibende Arbeit

- Tests wurden vor der Implementierung ergänzt. Core-Tests prüfen Pfade, Windows-Dateityp, macOS-Bundle-Argumente, ungültige Ziele und ausgeschlossene Dienste. Der native Tauri-IPC-Test lädt den gespeicherten Pfad, startet tatsächlich ein kompiliertes Testprogramm aus einem Verzeichnis mit Leerzeichen/Umlaut, prüft Arbeitsverzeichnis/Argumentzahl und genau einen Prozessstart, wiederholt den Start und prüft Journal sowie weiterhin getrenntes OBS.
- UI-Tests prüfen Startfehler/Wiederholung, Trennung von Start und Verbindung, Connecting-Sperre, Providerwechsel, gemeinsame Verbindungskommandos, Navigation, Test-Alert/Null-Ergebnis, Ladefehler und Layout-/Fokusregeln. Settings-Tests sichern die Bearbeitung des Spotify-Pfads mit erhaltenen Legacy-Feldern ab. Ein instabiler Teststart wurde durch explizites Laden der verzögerten Serviceroute vor dem Rendern korrigiert.
- Vollständiger Rust-Workspace, 194 Frontend-Tests in 48 Dateien, Command-Vertrag, TypeScript und Produktionsbuild sind erfolgreich. Browserprüfung der produktiven Karte mit ausdrücklich simuliertem Backend bestätigt Startfehler, Wiederholung, Verbinden, Alert-Leerresultat und Providerwechsel. Sie startet weder Programme noch echte Dienste.

[Gemeinsame Musikaktionen](TAURI-DASHBOARD-MUSIC.md) sind inzwischen implementiert. Offen bleiben weitere Meldestellen, OBS4/OBS6, AL1/AL2 und die übrigen Meilensteine im [Umsetzungsplan](TAURI-IMPLEMENTATION-PLAN.md). Der native lokale Prozessnachweis wurde auf Windows durchgeführt; ein macOS-Bundle-Argumenttest ersetzt weder einen tatsächlichen Bundle-Start noch die installierte Gesamtabnahme. DA2 bleibt teilweise umgesetzt.
