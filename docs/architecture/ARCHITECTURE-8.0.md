# Creator Control Suite 8.x – Ist- und Zielarchitektur

Stand: 28. Juli 2026

## Systemkontext

```mermaid
flowchart LR
    creator["Creator / Operator"] --> app["Creator Control Suite WPF"]
    app --> obs["OBS WebSocket"]
    app --> twitch["Twitch APIs / EventSub"]
    app --> spotify["Spotify APIs"]
    app --> agent["Creator Control Agent (LAN/HTTPS)"]
    app --> overlay["Lokaler Overlay-Webserver"]
    obs --> browser["OBS Browser Source"]
    browser --> overlay
    app --> github["GitHub Releases / signierte Updates"]
    streamdeck["Stream Deck / IPC-Client"] --> ipc["Named Pipe IPC"]
    ipc --> app
```

## Container und Verantwortungen

```mermaid
flowchart TB
    shell["App Shell\nNavigation, Window-Lifecycle"] --> appservices["App Services\nUse Cases und Orchestrierung"]
    appservices --> contracts["Core\nContracts, Policies, Configuration"]
    appservices --> modules["Integrationsmodule\nOBS, Twitch, Spotify, Alerts, Overlay"]
    modules --> contracts
    workflow["Workflow-Modul"] --> capabilities["Workflow-eigene Capability-Ports"]
    appservices --> adapters["OBS-/Music-/Alert-/Overlay-Adapter"]
    adapters --> capabilities
    adapters --> modules
    agent["Agent\nversionierte HTTPS API"] --> agentsecrets["DPAPI Secret Store"]
    updater["Updater\ntransaktionales Apply/Rollback"] --> safezip["Safe ZIP Boundary"]
```

Die heutige Hauptschuld ist die Shell: `MainWindow.xaml.cs` bündelt UI,
Persistenz, Netzwerkzugriffe, Timer und Domänenorchestrierung. Sie wird
strangler-artig in vertikale Features zerlegt. Neue Features dürfen diese
Schuld nicht vergrößern.

Die Anwendung enthält keine kommerzielle Laufzeit-Lizenzierung mehr. Editionen,
Aktivierung, Lizenzserver und Feature-Gates wurden entfernt; alle Funktionen
und Themes sind verfügbar. Die Wahl einer Open-Source-Lizenz bleibt eine
rechtliche Repository-Entscheidung und ist kein Laufzeitdienst.

## Sicherheitsrelevante Datenflüsse

| Fluss | Geheimnisse | Schutzgrenze |
|---|---|---|
| OAuth Twitch/Spotify | Tokens, Client-Secrets | `ISecretStore`/DPAPI; keine Logs |
| App → Agent | Geräte-API-Key, OBS-Passwort | Nutzerbestätigtes TLS-Pinning, versionierte API, DPAPI |
| IPC → App | lokale Befehle | Named Pipe, Validierung, begrenzte Befehlsmenge |
| App → Overlay | Stream-/Chatdaten | Loopback/LAN-Konfiguration, Payload-Limits |
| Release → Updater | Manifest, ZIP | RSA-Signatur, SHA-256, sichere Extraktion |

## Zielgrenzen

- `MainWindow`: höchstens 500 Zeilen Code-behind.
- Seiten: eigene View, ViewModel und Anwendungsservice; XAML unter 1.000 Zeilen.
- `App.xaml.cs`: Composition Root und Lifecycle werden getrennt.
- Workflow verwendet nur Capability-Interfaces, keine konkreten Module.
- Modulregistrierung liegt beim jeweiligen Modul.
- Einstellungen behalten die JSON-Kompatibilität, erhalten aber Schema-Version
  und sequenzielle Migrationen.
- Die Konfigurationsmodelle sind nach General, OBS, Twitch, Music, Alerts,
  Overlay, Workflow, StreamDeck, Dashboard und Updates getrennt; `AppSettings`
  bleibt ausschließlich der kompatible Top-Level-Vertrag.
- Lang laufende Komponenten implementieren `IHostedService` oder einen
  äquivalent testbaren Lifecycle.
- Persistierte Einstellungen tragen eine Schema-Version und werden ausschließlich
  über sequenzielle, getestete Migrationen angehoben; Zukunftsschemas werden
  ohne stilles Downgrade abgelehnt.

## Verifikation

`ArchitectureGuardTests` blockiert neue/gesteigerte Größenschuld,
Core-Rückreferenzen, Referenzen von Modulen auf die App und die
Wiedereinführung kommerzieller Laufzeit-Lizenzierung. Bestehende
Überschreitungen sind als schrumpfende Baseline erfasst und dürfen nicht wachsen.
Der Named-Pipe-Vertrag wird als echter Client/Server-Roundtrip einschließlich
Fehlerisolation und Lifecycle getestet.
Der Spotify-Web-API-Vertrag wird gegen versionierte 2026-Fixtures geprüft:
Playback-Nullability, Bearer-Header, Paging, URL-Encoding, generische
Bibliotheksendpunkte und die Umbenennung von Playlist-`tracks` zu `items`.
Der Twitch-Helix-Vertrag wird analog über Fixtures für Pflicht-Header,
Benutzer-Mapping, Follower-Query, cursor-basiertes Chatter-Paging,
Chat-Drop-Reasons und Fehlerantworten abgesichert.
Der OBS-WebSocket-5.x-Vertrag wird über reale JSON-Frame-Fixtures für
Handshake, Authentifizierung, Request-Status und Events abgesichert. Ein
zentraler Codec validiert Envelope-Struktur und ein 4-MiB-Payload-Limit.
OBS-Transport, Twitch-API-Verträge und Spotify-Token-/Playback-Helfer liegen in
separaten Dateien; keine dieser Integrationsdateien überschreitet 1.000 Zeilen.
Canary-Steuerung, Fehlergrenze, Wartungsfenster und Agent-Transport für
Multi-PC-Updates liegen im testbaren `RemoteUpdateRolloutService` und nicht mehr
im Window-Code.
Die übrige Multi-PC-Oberfläche ist sichtbar als **Alpha** gekennzeichnet und
wird auf Produktentscheidung vorerst nicht weiter refaktoriert. Sie gehört
damit nicht zu den als stabil zugesagten Verkaufsfunktionen.
Legacy-Normalisierung und validierungsgeschützte Persistenz der Einstellungen
liegen im `SettingsApplicationService`; ungültige Einstellungen erreichen den
persistenten Store nicht.
Der lokale Updateablauf wird durch `UpdateWorkflowService` orchestriert:
Download, optionales Backup, Apply und Fortschrittsphasen sind unabhängig von
WPF testbar.
Der Settings-Tab hostet für lokale Updates eine eigene
`UpdateSettingsView` mit `UpdatePageViewModel`. Prüfung, Installation,
Backup-Liste und Restore-Zustand liegen damit nicht mehr in `MainWindow`.
Legacy-Erkennung und -Import liegen analog in `MigrationSettingsView` und
`MigrationPageViewModel`; die Shell stellt nur den Reload-Callback nach einem
erfolgreichen Import bereit.
Rechtliche Dokumente werden über `LegalSettingsView`,
`LegalPageViewModel` und den testbaren `ILegalDocumentLauncher` geöffnet; die
Shell kennt weder Dokumentpfade noch Prozessstart.
Allgemeine Angaben, Theme-Auswahl und Verbindungs-Watchdog liegen in
`GeneralSettingsView` und `GeneralSettingsPageViewModel`. Mapping,
Intervallgrenzen und Live-Themewechsel sind unabhängig von der Shell testbar.
Der Twitch-Ziele-Editor liegt in `TwitchGoalsView` und
`TwitchGoalsPageViewModel`. Settings-Mapping, Eingabenormalisierung und die
Übernahme der Live-Zähler sind damit unabhängig von WPF testbar; die
Overlay-Orchestrierung verbleibt bis zu einem späteren Slice in der Shell.
Die Spotify-Automatik liegt in `SpotifyAutomationView` und
`SpotifyAutomationPageViewModel`. Start-/Endmusik, Live-Lautstärke,
Alert-Ducking, Fade-Grenzen und Startplaylist werden dort normalisiert und
getestet; Wiedergabe und Spotify-API-Orchestrierung verbleiben zunächst in der
Shell.
Die Workflow-Sessionanzeige liegt in `WorkflowSessionView` und
`WorkflowSessionPageViewModel`. Status, Countdown und Sessionmetriken werden
ohne WPF-Abhängigkeit abgebildet; Reset und Viewer-Samples laufen über
testbare Commands und schmale Shell-Callbacks.
Webserver- und Chat-Einstellungen des Overlays liegen in
`OverlayConnectionSettingsView` und
`OverlayConnectionSettingsPageViewModel`. Portvalidierung,
Darstellungsnormalisierung, URL-Aktionen und Dateiauswahl sind damit vom
Canvas-/Extension-Pack-Lifecycle getrennt und unabhängig von WPF testbar.
Canvas-Liste, URLs und Benutzeraktionen liegen in `OverlayCanvasView` und
`OverlayCanvasPageViewModel`; `OverlayCanvasApplicationService` koordiniert
Layout-, Settings- und Webserver-Persistenz. Create und Duplicate rollen bei
einem fehlgeschlagenen Settings-Write zurück. Delete persistiert zuerst die
Metadaten und entfernt danach die Layoutdatei, damit kein gespeichertes Canvas
ohne Layout entsteht.
Extension-Pack-Katalog, Import und Deinstallation liegen in
`OverlayExtensionPacksView` und `OverlayExtensionPacksPageViewModel`.
Alert-Bibliothek und Definition-Lifecycle liegen in `AlertLibraryView`,
`AlertLibraryPageViewModel` und `AlertDefinitionApplicationService`.
Create, Duplicate, Enable/Disable und Delete werden unabhängig von WPF
persistiert und bei fehlgeschlagenem Settings-Write zurückgerollt. Das
`AlertDefinitionEditorViewModel` übernimmt Designer-Mapping, Normalisierung
und Validierung; MediaElement, Dateidialoge und die Laufzeitvorschau verbleiben
als Plattformadapter in der Shell.
`AlertRuntimeView` und `AlertRuntimePageViewModel` kapseln zusätzlich
Alert-Aktivierung,
Streamer.bot-Unterdrückung, OBS-Quellnamen, Zwischenpause und die Darstellung
des Queue-Zustands. Benutzeraktionen werden als Commands an schmale
OBS-/Streamer.bot-Plattformadapter delegiert; die View enthält keine
Geschäftslogik.
Der Agent-Composition-Root liegt unter 1.000 Zeilen; Hosting, Discovery und
Datei-/Update-Helfer sind in `AgentUtilities` isoliert.
