# CastingCouch 8.0.0-beta1

Erster öffentlicher Beta-Release nach der Alpha-Serie (`8.0.0-alpha102`).

## Branding & Produkt

- Anzeigename und Produktbranding auf **CastingCouch** umgestellt.
- Kompaktere Titelleiste und bessere Lesbarkeit im Dashboard.
- Neues App-Theme „biomilchs Bubatz Cantina“ sowie Verbesserungen an Theme-Farben und Settings-Tabs.

## Overlay

- Persistente Chat-Historie und reichere Chat-Widgets (inkl. Twitch-Farben, Kapazität, Clear/Delete).
- Overlay-Asset-Library, Editor-Interaktionen und themed Widgets.
- Extension-Pack-Widgets, -Effects und -Animations im Editor und Live-Overlay nutzbar.
- Overlay-Architektur refaktoriert; obsolete Pfade entfernt.

## Stabilität

- Start-Deadlock behoben: Chat-History-Kapazität lädt Layout/Settings async auf dem UI-Thread
  (kein `GetResult()` mehr → Fenster erscheint wieder zuverlässig).
- Architecture-Guard ignoriert lokale `bin`/`obj`/`artifacts`-Buildreste unter `src/`.
- Canvas-Overlay-Bundles werden erst nach dem TypeScript-Build als Embedded Resources eingebunden
  (saubere CI-Checkouts behalten Runtime/Editor-Assets).
- Twitch-History-Kennzahlen formatieren fest mit `de-DE` (kein CI-Culture-Drift mehr).
- Overlay-Typecheck: entferntes TypeScript-`baseUrl` für TS 7.
- Release-Layout: kein `BundledOverlay`-Ordner mehr erforderlich (Overlays embedded).

## Architektur

- Weitere Extraktion von MainWindow-Services (OBS, Twitch, Stream Deck, StreamerBot, Timed Automation, RunOfShow).
- Agent-API-Endpoints extrahiert und Verträge gehärtet.
- Architecture-Gates für Agent-Composition und MainWindow-Service-Slices.

## Dependencies & CI

- GitHub Actions und zentrale NuGet-/npm-Abhängigkeiten aktualisiert
  (u. a. Microsoft.Extensions, Test-SDK, ProtectedData, TypeScript, esbuild).
