# OBS-Suchfilter und Eingangsfilter

Stand: 2026-10-03. Implementiert und automatisiert geprüft. Installierte OBS-/Windows-/macOS-Abnahme bleibt offen.

## Referenz und Bedienung

Die Referenz ist `src/CreatorControlSuite.App/Shell/Services/Obs/MainWindow.Services.Obs.ServiceSources.cs`, insbesondere `ApplyServicesObsSceneFilter`, `ApplyServicesObsSourceFilter`, `ApplyServicesObsInputFilter` und `ClassifyObsAudioInput`. Die Kategorien entsprechen `Views/Pages/Services/ObsServiceView.xaml`.

In der produktiven OBS-Verwaltung stehen drei voneinander unabhängige Suchfelder zur Verfügung. Die Suche ignoriert Groß-/Kleinschreibung und äußere Leerzeichen:

- Szenen: Suche im Namen; aktuelle Programmszene zuerst, danach OBS-Index. Eine weiterhin sichtbare Auswahl bleibt erhalten, andernfalls wird die sichtbare aktuelle Programmszene gewählt oder die Auswahl geleert. Eine Auswahl ändert nicht die live geschaltete OBS-Szene.
- Quellen: Suche im Namen, `sourceType` und vorhandenem `inputKind`; Reihenfolge weiterhin anhand der tatsächlichen Scene-Item-Indizes. Die Suche gilt auch innerhalb geöffneter Gruppen. Eine ausgeblendete Auswahl wird entfernt. Positionsänderungen verwenden weiterhin die vollständige Item-Anzahl und tatsächlichen IDs, nicht die verkürzte Suchliste.
- Eingänge: Suche in Name und `inputKind`, zusätzlich alle/Mikrofon/Spiel bzw. Desktop/Musik/Browser bzw. Alerts/stumm. Sortierung nach Kategorie und Namen; eine weiterhin sichtbare Auswahl bleibt erhalten, sonst wird der erste Treffer gewählt. Ohne Treffer gibt es keine Eingangsbedienung.

Die Filter sind flüchtige UI-Zustände und ändern keine Einstellungen, OBS-Namen oder Layoutdateien.

## Native Daten und Fehler

Der typisierte `ObsQuery::InputCatalog` verwendet dieselbe native `obs_query`-Fachlogik wie andere OBS-Abfragen. Er liest `GetInputList`, ergänzt die ursprünglichen Eingangsobjekte um `category`, `inputMuted` und `muteError` und liest den aktuellen Mute-Zustand über `GetInputMute`. Unabhängige Mute-Abfragen laufen mit höchstens acht gleichzeitigen Requests; die Ergebnisreihenfolge bleibt die OBS-Katalogreihenfolge. Der bestehende rohe `inputs`-Vertrag bleibt erhalten.

Die Kategorieheuristik entspricht C#: Mikrofonbegriffe haben Vorrang vor Musik, danach Browser/Alerts, sonst Spiel/Desktop; Name, Kind und unversioniertes Kind werden berücksichtigt. Die Kategorien ersetzen keinen Nachweis tatsächlicher Audiofähigkeiten. Auch Bild-/Videoeingänge können in OBS keine Audioeigenschaften besitzen.

Ein fehlgeschlagener oder nicht als Boolean gelieferter Mute-Zustand bleibt `null`. Der Stummfilter enthält nur ausdrücklich bestätigte `true`-Werte und zeigt die Fehler der unbekannten Eingänge. Ein Fehler der gesamten Katalog-/Szenen-/Quellenabfrage darf alte Daten nicht weiter zur Bedienung verwenden. Manueller Refresh ermöglicht Wiederherstellung; der Eingangskatalog wird außerdem alle fünf Sekunden, die aktuelle Programmszene alle drei Sekunden abgefragt.

Scene-Item-Auswahl verwendet sowohl ID als auch Namen. Wird dieselbe ID für eine andere Quelle geliefert, bleibt diese nicht versehentlich ausgewählt. Szenen- und Gruppenwechsel verwerfen die vorherige Scene-Item-Auswahl. Während einer tatsächlichen Mutation bleiben Auswahl und Filter gesperrt. Medien-/Browsermutationen prüfen weiterhin unmittelbar vor dem Befehl die aktuelle Quellenart in Rust.

## Nachweise und verbleibender Umfang

- Zuerst vier fehlgeschlagene UI-Tests und ein fehlgeschlagener OBS-WebSocket-Test belegen die fehlenden Filter/Katalogabfrage und veraltete Scene-Item-Bedienung nach Abfragefehlern.
- Die produktive UI prüft Suchfelder, aktuelle Szenenpriorität, erhaltene Auswahl, ausgeblendete Auswahl, Typensuche, Kategorien, unbekannte Mute-Werte, Abfragefehler/Refresh, wiederverwendete IDs und Gruppensortierung mit vollständiger Item-Anzahl.
- Modulprüfung verwendet eine tatsächliche lokale OBS-v5-WebSocket-Gegenstelle für Namen, Kategorien, Mute-Fehler, erfolgreiche Abfragen und Trennung. Native Tauri-IPC prüft `input_catalog`, Unicode-Namen, versionierte Kinds, tatsächliche Boolean-Werte sowie Teilerfolg nach Quellenartwechsel und fehlgeschlagener Mute-Abfrage.
- Vollständiger Rust-Workspace, 214 Frontend-Tests in 49 Dateien, Contract-/TypeScript-Prüfung und Produktionsbuild erfolgreich. Separate Browser-Testfixture bestätigt Darstellung, Auswahlwechsel, Abfragefehler und Wiederherstellung ohne tatsächliche Dienstverbindung.

OBS4 ist damit gemäß der belegten C#-Restliste implementiert; installierte Betriebsabnahme ist weiter offen. OBS5-Kategorieaktionen für Gruppen-Mute, Solo und gemeinsame Lautstärke sind ein eigener verbleibender Ablauf. AL1/AL2 und die späteren Meilensteine bleiben im [Umsetzungsplan](TAURI-IMPLEMENTATION-PLAN.md) erhalten. Allgemeine Filterparameter-/Quellentypeditoren werden ohne tatsächlich implementierte C#-Referenz nicht als zusätzliche Paritätsanforderung vorausgesetzt.
