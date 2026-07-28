# Changelog 8.0.0-alpha102 – Architektur-Review-Umsetzung

## Tests

- Substanztests für `StreamWorkflowService`, `AlertEngine` und die ehemals
  vorhandene Laufzeit-Lizenzierung
- Table-driven `SettingsValidator`-Coverage für alle Error-Codes
- Tests für `AutomationRuleEngine`, `SecretJsonStore` und `EventBus`

## Erweiterbarkeit / Wartbarkeit

- `IAlertRenderer` für mockbare Alert-Wiedergabe
- `IModuleRegistration` + `AddStreamingModules()` statt Inline-DI
- Extrahierte Services: `MultiPcAgentClient`, `StreamerBotClient`,
  `MusicPlayerUiPresenter`
- MVVM-Fundament (`NavigationService`, `DiagnosticsPageViewModel`)
- `SecretJsonStore<T>`, `AutomationRuleEngine`, `NowPlayingWidget`
- Agent: `WithObsControl`-Helper für OBS-Routen
- `IEventBus` nach Core verschoben und über `AppEventBridge` verdrahtet

## Twitch-Ziele

- Der Twitch-Ziele-Editor besitzt eine eigene View und ein eigenes ViewModel.
- Settings-Mapping, Eingabenormalisierung und Live-Zähler sind unabhängig von
  `MainWindow` getestet.
- Die Architektur-Baseline für `MainWindow` wurde ausschließlich nach unten
  angepasst.

## Spotify-Automatik

- Start-/Endmusik, Live-Lautstärke, Alert-Ducking und Startplaylist besitzen
  eine eigene View und ein eigenes ViewModel.
- Eingabegrenzen, Playlist-Auswahl und Persistenz-Callback sind unabhängig von
  WPF getestet.
- Laufende Alert-Statusmeldungen werden über das ViewModel statt über
  Shell-Steuerelemente aktualisiert.

## Workflow-Session

- Sessionstatus, Countdown und Statistiken besitzen eine eigene View und ein
  eigenes ViewModel.
- Reset und Viewer-Samples werden über Commands an schmale
  Anwendungs-Callbacks delegiert.
- Vier ViewModel-Tests sichern Mapping, leere Szenen, Eingabevalidierung und
  Delegation ab.

## Overlay-Verbindung

- Webserver- und Chat-Einstellungen besitzen eine eigene View und ein eigenes
  ViewModel.
- Portvalidierung, Darstellungsnormalisierung, URL-Aktionen und
  Hintergrundauswahl sind aus `MainWindow` entfernt.
- Sieben ViewModel-Testfälle sichern Mapping, Grenzwerte, fehlerhafte Ports
  und UI-Delegation ab.

## Overlay-Canvas und Extension Packs

- Canvas-Liste, URLs, Editorstart und CRUD-Aktionen besitzen eine eigene View
  und ein eigenes ViewModel.
- `OverlayCanvasApplicationService` koordiniert Settings, LayoutStore und
  Webserver ohne WPF-Abhängigkeit.
- Create und Duplicate entfernen bei fehlgeschlagener
  Settings-Persistenz ihre Layoutdateien und Metadaten wieder.
- Delete speichert zuerst die neue Canvas-Liste und entfernt danach das
  Layout; ein Settings-Fehler lässt das bestehende Canvas vollständig intakt.
- Extension-Pack-Katalog, ZIP-Import und Deinstallation besitzen eine eigene
  View und ein eigenes ViewModel.
- Dreizehn neue Service-/ViewModel-Tests sichern Lifecycle, Teilfehler,
  Bestätigungen, URL-Mapping und Dateifehler ab.

## Alert-Bibliothek und Designer

- Alert-Liste, Auswahl sowie Create-, Duplicate-, Enable- und Delete-Aktionen
  besitzen eine eigene View und ein eigenes ViewModel.
- `AlertDefinitionApplicationService` persistiert Definitionen unabhängig von
  WPF und stellt den vorherigen Zustand bei fehlgeschlagener Persistenz wieder
  her.
- Das Designer-Mapping und die Eingabevalidierung liegen im
  `AlertDefinitionEditorViewModel`; die Shell behält nur MediaElement,
  Dateidialoge und Vorschau-Orchestrierung.
- Alert-Quelle, Streamer.bot-Unterdrückung, OBS-Quellnamen,
  Zwischenpause und Queue-Status liegen im `AlertRuntimePageViewModel`.
- Die zugehörigen Bedienelemente liegen in einer eigenen
  `AlertRuntimeView`; Refresh, Unterdrückung, Queue-Steuerung und
  OBS-Installation werden über testbare Commands an Plattformadapter
  delegiert.
- Dreiundzwanzig neue Tests sichern Bibliotheksaktionen, Rollbacks,
  Bestätigungsdialog-Delegation, Feld-Mapping und numerische Grenzwerte ab.

## Stream-Statistik

- Die vollständige Statistikseite liegt in `StatisticsPageView` und
  `StatisticsPageViewModel` statt in `MainWindow.xaml`.
- JSONL-Parsing, beschädigte Datensätze, gewichtete Zuschauerdurchschnitte,
  Kategorien und Verlaufsreihen liegen im
  `StreamStatisticsApplicationService`.
- Refresh, Ordneröffnung und Auswahl der Dashboard-Kennzahl werden über
  Commands beziehungsweise schmale Shell-Callbacks delegiert.
- Vier neue Tests sichern Projektion, Leerzustand, Dateiladen und
  Kennzahlwechsel ab.

## IPC-Vertrag

- Echte Named-Pipe-Client/Server-Tests prüfen Roundtrip, JSON-Fehlerisolation
  und idempotenten Lifecycle.
- Die lokale IPC-Dokumentation beschreibt den aktuellen v1-Zeilenvertrag und
  alle unterstützten Befehlsgruppen.

## Spotify-Web-API 2026

- Bibliotheksaktionen verwenden die generischen `/me/library`-Endpunkte und
  Spotify-URIs.
- Playlist-Inhalte verwenden `/items`; neue `items`- und ältere
  `tracks`-Antwortfelder werden kompatibel gelesen.
- Nullable Playback-Felder und Nicht-Track-Inhalte führen nicht mehr zu
  fehlerhaften Track-Modellen oder JSON-Ausnahmen.
- Fünf Fixture-basierte Contract-Tests sichern Authentifizierung, Mapping,
  Paging, Encoding und aktuelle Routen ab.
- Die kommerzielle und Streaming-Nutzung des Spotify-Moduls ist als eigener
  Verkaufsblocker im Risk Register erfasst.

## Twitch-Helix-Vertrag

- Fünf Fixture-basierte Contract-Tests prüfen Pflicht-Header, Benutzer-Mapping,
  Follower-Zahlen, Chatter-Paging, Chat-Drop-Reasons und HTTP-Fehler.
- Der Follower-Aufruf sendet keinen von Helix nicht unterstützten
  `moderator_id`-Query-Parameter mehr.
- Chatter werden cursor-basiert über mehrere Seiten geladen, dedupliziert und
  stabil sortiert.

## OBS-WebSocket-Vertrag

- Sechs OBS-5.x-Frame-Fixtures sichern Handshake, Authentifizierung,
  Request-/Response-Status und zentrale Events ab.
- Ein testbarer Protokoll-Codec validiert Envelope-Struktur und begrenzt
  eingehende Payloads auf 4 MiB, einschließlich einzelner großer Frames.
- Die Handshake-Erzeugung ist aus dem Client extrahiert und lehnt nicht
  unterstützte RPC-Versionen sowie unvollständige Challenges explizit ab.
- 13 Contract-Testfälle decken valide, fehlerhafte und übergroße Frames ab.

## Multi-PC

- Multi-PC ist in Navigation, Seite und Dokumentation als **Alpha**
  gekennzeichnet.
- Die weitere Multi-PC-Refaktorierung ist bis zu einer neuen
  Produktentscheidung bewusst zurückgestellt.
