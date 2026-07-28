# Changelog 8.0.0-alpha102 – Architektur-Review-Umsetzung

## Tests

- Substanztests für `StreamWorkflowService`, `AlertEngine` und die ehemals
  vorhandene Laufzeit-Lizenzierung
- Table-driven `SettingsValidator`-Coverage für alle Error-Codes
- Tests für `AutomationRuleEngine`, `SecretJsonStore` und `EventBus`
- Die globale Coverage-Ratsche wird um ein CI-blockierendes
  90-%-Branch-Gate für sicherheits- und updatekritische Dateien ergänzt.
- Die globale Ratsche basiert nun auf dem reproduzierten Release-Messwert von
  21,13 % Lines und 12,86 % Branches statt auf einer unbelegten Zielzahl.
- `FileUpdateTransaction` erreicht mit der automatisierten Abbruch-,
  Recovery- und Retry-Matrix 92 von 92 abgedeckte Branches.

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

## Overlay-Runtime

- Die 1.232-zeilige `core-functions.ts` ist auf einen 719-zeiligen
  Kompositionskern reduziert.
- Chat- und Socials-DOM, Mapping und Rendering liegen in ihren bestehenden
  Widget-Modulen statt in der zentralen Runtime-Datei.
- Direkte DOM-Tests sichern Escaping und Deduplizierung an den neuen
  Modulgrenzen.
- Das Architekturgrößen-Gate erfasst jetzt auch alle TypeScript-
  Produktionsquellen und blockiert Dateien ab 1.000 Zeilen.

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

## Music Player

- Die vollständige Music-Player-Seite liegt in `MusicPlayerPageView` statt in
  `MainWindow.xaml`.
- Wiedergabe, Verbindung, Seek, Lautstärke, Bookmarklet-Aktionen und
  Drag-and-drop werden über `MusicPlayerPageActions` an schmale
  Anwendungs-Callbacks delegiert.
- `MainWindow` greift auf keine Steuerelemente der Music-Player-Seite mehr
  direkt zu; ein Architekturtest sichert diese Grenze.
- Die schrumpfende Architektur-Baseline liegt nun bei 23.552 Zeilen
  Code-behind und 3.796 Zeilen XAML.

## Workflow-Seite

- Die vollständige Workflow-Seite liegt in `WorkflowPageView` statt in
  `MainWindow.xaml`.
- Regieplan, zeitgesteuerte Automationen, Workflow-Designer und Stream-Kurztest
  sind als eigene Views mit jeweils deutlich weniger als 1.000 Zeilen
  extrahiert.
- Die globalen Workflow-Aktionen werden über `WorkflowPageActions` delegiert;
  die Shell kennt weder die Seitenbuttons noch deren Statusfeld.
- Die Dashboard-Kurztestaktion öffnet nun den tatsächlichen Kurztest-Tab.
- Regieplan, Automation-Editor/Kurztest und Workflow-Designer sind zusätzlich
  in drei Partial-Dateien unterhalb des 1.000-Zeilen-Gates aufgeteilt.
- Die Architektur-Baseline sinkt auf 21.814 Zeilen Code-behind und
  3.392 Zeilen XAML.

## Einstellungen

- Die vollständige Einstellungen-Seite liegt in `SettingsPageView` statt in
  `MainWindow.xaml`; vorhandene General-, Legal-, Update- und Migration-Views
  werden dort komponiert.
- Save, Statusanzeige und Tabnavigation sind hinter der öffentlichen View-API
  gekapselt.
- Die Streamer.bot-Alert-Bindungen hängen nicht mehr vom Namescope des
  Hauptfensters ab.
- Laden, Legacy-Migration und Speichern liegen in der 547-zeiligen Partial
  `MainWindow.Settings.Persistence.cs`.
- Die Architektur-Baseline sinkt auf 21.337 Zeilen Code-behind und
  2.913 Zeilen XAML.

## Dienste

- Die bisher 1.326-zeilige Dienste-Seite ist in eine 92-zeilige
  `ServicesPageView` und getrennte Views für Spotify, Twitch, OBS,
  Streamer.bot und Stream Deck zerlegt.
- Übersicht, Tabumschaltung und Service-Auswahl sind hinter der
  `ServicesPageView`-API gekapselt.
- Stream-Deck-Katalog/Runtime und Regelverwaltung/Export liegen in zwei
  Partial-Dateien mit 842 beziehungsweise 777 Zeilen.
- Keine neue Services-View oder Partial-Datei überschreitet 1.000 Zeilen.
- Die Architektur-Baseline sinkt auf 19.854 Zeilen Code-behind und
  1.591 Zeilen XAML.

## Dashboard

- Das vollständige Command-Center-Dashboard liegt in der 657-zeiligen
  `DashboardPageView` statt in `MainWindow.xaml`.
- `MainWindow.xaml` ist dadurch auf 943 Zeilen gefallen und benötigt keine
  Ausnahmeregel im Architekturgrößen-Gate mehr.
- Ein Architekturtest sichert den externen Dashboard-Host und die
  1.000-Zeilen-Grenze der Shell-XAML.

## Twitch-, Spotify- und OBS-Shell

- Twitch-Orchestrierung ist in drei begrenzte Partials für Dashboard/Raid,
  Engagement/Goals und API/Professional zerlegt.
- Spotify-Orchestrierung ist in Verbindung/Automation, Katalog/Geräte,
  Runtime-Overlay, Sichtbarkeit, Overlay-Einstellungen und sechs
  Saved-State-Verantwortungsbereiche zerlegt.
- OBS-Orchestrierung ist in Verbindung/Dashboard, Streamstart,
  Streamende-Planung, Streamende-Ausführung, Service-Quellen und
  Service-Steuerung zerlegt.
- Alle neuen Partial-Dateien bleiben unter 1.000 Zeilen; ein eigener
  Architekturtest sichert die Extraktionsgrenzen.
- Diagnose und Logs sind in zwei begrenzte Partials verschoben; Streamer.bot
  und Creator Intelligence besitzen eigene Integrations-Partials.
- Alert-Editor, Music-Runtime, Overlay-Runtime/Extensions,
  Dashboard-Runtime/Verbindungen sowie Workflow-Vorbereitung und
  Timed-Automation-Runtime sind ebenfalls physisch getrennt.
- Konstruktor/Event-Wiring ist nach Dashboard, Diagnose, OBS, Twitch,
  Spotify, Services, Workflow und Stream Deck in Initializer-Partials
  zerlegt.
- Navigation, Lifecycle, Release-Readiness, Dashboard-Layout und
  Szenenbuttons liegen in eigenen begrenzten Dateien.
- Die bestehende Multi-PC-Implementierung wurde ohne Logikänderung in zwei
  begrenzte Partials verschoben; die Alpha-Kennzeichnung bleibt bestehen.
- `MainWindow.xaml.cs` sinkt von 19.854 auf 495 Zeilen und erfüllt damit
  erstmals den Zielwert unter 500. Ein expliziter Architekturtest verhindert
  Regressionen.

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

## Update-Recovery

- Die Update-Transaktion protokolliert jede Zieldatei vor der Mutation als
  `PendingFile`.
- Recovery erfasst dadurch auch einen Prozess- oder Stromabbruch zwischen
  Dateikopie und Journal-Commit.
- Vorhandene Dateien werden aus dem Backup wiederhergestellt; unvollständig
  angelegte Neudateien werden entfernt.
- Ein zuvor fehlgeschlagener Rollback kann nach Freigabe gesperrter Dateien
  wiederholt werden, ohne dass alte Fehler den erfolgreichen Retry
  blockieren.
- Sieben fokussierte Tests decken Teilinstallation, Cancellation,
  Write-ahead-Recovery und Rollback-Retry ab. Der Windows-/MSI-E2E-Nachweis
  bleibt als Verkaufsfreigabe-Gate offen.
