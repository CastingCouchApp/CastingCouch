# OBS-Medienaktionen und Livemonitoring

Stand: 2026-10-03. Medien-Neustart/-Stopp, Browser-Neuladen und vorhandene C#-Monitoringwerte sind implementiert und automatisiert geprüft. Praktische OBS-Verbindungen sowie installierte Windows-/macOS-Abnahme bleiben offen.

## C#-Referenzen

`Shell/Services/Obs/MainWindow.Services.Obs.ServiceSources.cs` bietet `RestartSelectedObsMediaInputAsync`, `StopSelectedObsMediaInputAsync` und `RefreshSelectedObsBrowserInputAsync`. Medienquellen werden anhand `ffmpeg`, `vlc` oder `media` erkannt, Browser anhand `browser`. Der C#-Client sendet `TriggerMediaInputAction` mit `OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART` beziehungsweise `STOP`, und `PressInputPropertiesButton` mit `refreshnocache`.

`MainWindow.Services.Obs.ConnectionDashboard.cs`, `RefreshObsProfessionalControlAsync`, zeigt Stream-/Aufnahmezeitcode, Aufnahmepause, Replay Buffer, virtuelle Kamera, CPU, FPS, RAM, Render-Skipped/Total und Output-Skipped/Total. Diese tatsächlich vorhandenen Werte bilden die Monitoringreferenz.

## Medien- und Browserbedienung

Die bestehende Quellenverwaltung bietet je nach aktueller Quellenart die passende Bedienung. Die typisierten `ObsControl`-Varianten `restart_media`, `stop_media` und `refresh_browser` benötigen `inputName`. Der generierte TypeScript-Vertrag prüft Namen und Argumente; eine beliebige Properties-Schaltfläche wird darüber nicht freigegeben.

Der native `ObsClient::control` liest vor jeder dieser Mutationen erneut `GetInputList` und prüft den exakten Quellennamen sowie `unversionedInputKind` beziehungsweise `inputKind`. Leere Namen, fehlende Quellen, unbekannte Antwortformate und inzwischen geänderte Quellenarten verhindern den eigentlichen Befehl. Ein alter UI-Snapshot genügt nicht als Quellenartnachweis. Es werden keine Quellen angelegt, umbenannt oder Layout-/Settings-Dateien geändert. Intern benötigte Alert-Renderer-Operationen bleiben erhalten.

Während der tatsächlichen Mutation bleiben Quellenwechsel und weitere Medienaktionen gesperrt. Erst die angenommene OBS-Antwort erzeugt eine Meldung „angefordert“; ein Fehler bleibt sichtbar und erlaubt einen erneuten bewussten Versuch. OBS-Annahme beweist noch keine sichtbar laufende Mediendatei. Native Erfolgs-/Fehlermeldungen erreichen dasselbe App-Journal wie andere Dienste. Eine fehlgeschlagene Quellenlistenabfrage versteckt Aktionen mit alten Quellenarten; „OBS-Verwaltung aktualisieren“ ermöglicht eine neue Abfrage.

## Monitoringvertrag

`obs_output_status` liest `GetStreamStatus`, `GetRecordStatus`, `GetReplayBufferStatus`, `GetVirtualCamStatus` und `GetStats` unabhängig. Jeder fehlgeschlagene Teil erhält `null` und `errors.<teil>`; auch ein Streamabfragefehler darf erfolgreiche Aufnahme-/Statistikdaten nicht verwerfen. Live-Daten und Sitzungserfassung erhalten bei unbekanntem Stream weiterhin keinen erfundenen Offlinezustand.

`ObsControls` in Dashboard und Dienste aktualisiert alle drei Sekunden und nach angenommenen Ausgangsmutationen. Die gemeinsame Monitoringanzeige übernimmt die Zeitcodes; fehlt ein Zeitcode, wird eine tatsächlich bekannte `outputDuration` in Millisekunden als `HH:MM:SS` angezeigt. Fehlende, negative oder nicht endliche Kennzahlen bleiben unbekannt. Render-/Encoding-Lag benötigt jeweils bekannte Skipped- und Total-Werte.

Eine vollständig fehlgeschlagene Snapshot-Abfrage oder eine getrennte Verbindung entfernt die veralteten Anzeigen und sperrt Ausgangsmutationen. Die UI zeigt keinen gestoppten Stream und keine Nullstatistiken als Ersatz für einen Fehler. „OBS-Status aktualisieren“ wiederholt die Abfrage ohne Mutation. Bei unbekannter Aufnahmepause ist Pause/Fortsetzen gesperrt; ein tatsächlich aktiver Aufnahmeausgang kann weiterhin gestoppt werden. Erfolgreiche andere Ausgänge bleiben bei einzelnen Abfragefehlern bedienbar.

## Nachweise und Restumfang

- Zuerst fehlgeschlagene UI-, Modul- und IPC-Tests belegen fehlende Mediencommands, fehlende Monitoringwerte und verworfene erfolgreiche Daten bei Streamabfragefehler. Ein nativer Tauri-IPC-Test verwendet eine wirkliche lokale OBS-v5-WebSocket-Gegenstelle, prüft exakte Requestfelder mit Unicode-Quellennamen, ursprüngliche Ablehnung und Retry, Quellenartwechsel, fehlende Quelle, falsche Argumentbenennung, Disconnect, erneute Verbindung, Journal und unabhängige Ausgänge. Verbindungswechsel im Test erfolgt ausdrücklich; das ist kein zusätzlicher Nachweis einer echten automatischen OBS-Wiederverbindung.
- UI-Tests prüfen die produktive Quellenverwaltung, passende Medien-/Browseraktionen, Fehlermeldung/Retry, Sperre während einer ausstehenden Antwort, fehlerhafte Quellenlisten mit altem Cache sowie sämtliche Monitoringfelder, unbekannte Pause, Snapshotfehler, Wiederherstellung und Trennung mit vorhandenen Cachedaten.
- Vollständiger Rust-Workspace, 214 Frontend-Tests in 49 Dateien, Command-Vertragsprüfung, TypeScript und Produktionsbuild erfolgreich. Eine getrennte Browser-Testfixture bestätigt Darstellung, Statusfehler/Wiederherstellung, Medienfehler/Retry/-Stopp und Browser-Neuladen mit simuliertem Backend. Sie verwendet keine tatsächliche OBS-Instanz.

OBS6 ist implementiert; Betriebsabnahme bleibt offen. OBS4 ist einschließlich der [Szenen-/Quellen-/Eingangsfilter](TAURI-OBS-SEARCH-FILTERS.md) implementiert; die installierte Betriebsabnahme bleibt offen. Audiokategorie-/Gruppenaktionen sind separat unter OBS5 zu übernehmen. Allgemeine Filterparameter- oder sämtliche quellentypspezifischen Editoren sind weiterhin keine belegte C#-Paritätsanforderung. AL1/AL2 und die übrigen Meilensteine bleiben im [Umsetzungsplan](TAURI-IMPLEMENTATION-PLAN.md) erhalten.
