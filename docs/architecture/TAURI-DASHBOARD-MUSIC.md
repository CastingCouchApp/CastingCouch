# Dashboard: gemeinsame Musikaktionen

Stand: 2026-10-03. Implementiert und automatisiert geprüft. Echte Spotify-/YouTube-Music-Wiedergabe sowie installierte Windows-/macOS-Abnahme bleiben offen.

## C#-Referenz und Bedienung

`Shell/Dashboard/MainWindow.Dashboard.Bindings.cs` verbindet vorherigen/nächsten Titel, Play/Pause, Seek, Shuffle und Repeat. `Shell/Services/Spotify/MainWindow.Services.Spotify.CatalogDevices.cs`, `RefreshSpotifyQuickPlaylists` und `StartSpotifyPlaylistAndRememberAsync`, verwendet Favoriten vor zuletzt gestarteten Playlists, dedupliziert URIs ohne Beachtung der Großschreibung und erhält die ausgewählte Playlist bei Aktualisierungen. Die Liste wird gegen den tatsächlichen Playlistkatalog gefiltert; gelöschte oder nicht verfügbare Einträge werden nicht angeboten. Der native Verlauf behält höchstens fünf zuletzt gestartete Playlists.

Der vorhandene `CommonMusicPlayer` ist auf Dashboard und Musikseite montiert. `SpotifyQuickControls` ergänzt Schnellplaylist, Shuffle und Repeat auf beiden Seiten. Die bisherige zweite Shuffle-/Repeat-Implementierung auf der Musikseite entfällt. `spotify-catalog.ts` liefert derselben Bibliothek und den Schnellaktionen dieselbe Auswahl-/URI-Logik. Bestehende Settings-Felder, Commands und OAuth-/HTTP-Verträge bleiben erhalten.

## Laufzeit und Fehler

| Bedienung | Gemeinsamer nativer Vertrag |
|---|---|
| Play/Pause, vorheriger/nächster Titel, Seek, Lautstärke | `music_player_action`, bestehende Provider-/Szenenmusik-/Ducking-Sperren |
| Schnellplaylist starten | `spotify_action(play_playlist)` mit vorhandenem bevorzugtem Gerät, Playlist-Shuffle-Präferenz und anschließender Verlaufsspeicherung |
| Shuffle / Repeat | `spotify_action(shuffle/repeat)`; Anzeige aus `spotify_query(playback)` |
| Einstellungen / Playlistkatalog | `get_settings`, `spotify_query(all_playlists)`; gemeinsame Query-Caches mit Musikbibliothek |

Spotify-Schnellaktionen werden ausschließlich für den gewählten, verbundenen Spotify-Provider angeboten. Bei Providerwechsel, fehlendem Status oder laufender Mutation sind sie gesperrt; YouTube Music erhält keine Spotify-Controls und keine zusätzlichen Spotify-Abfragen. Unbekanntes Shuffle/Repeat wird als unbekannt angezeigt und bleibt gesperrt. Eine fehlgeschlagene Statusabfrage darf vorhandene Cache-Werte nicht als aktuell nutzbar darstellen. Dasselbe gilt nun für den gemeinsamen Player: Ein Snapshot-Abfragefehler sperrt die Bedienung und zeigt unbekannte Wiedergabe statt eines veralteten Titels.

Nach angenommenen oder abgelehnten Mutationen werden Settings, gemeinsame Musikdaten, Playback und Geräte aktualisiert. Fehler bleiben sichtbar, die Auswahl bleibt erhalten und erneuter Start ist möglich. Die Schaltfläche „Spotify-Optionen aktualisieren“ wiederholt die Abfragen ohne Wiedergabebefehl. Ein vom API angenommener Playliststart mit anschließend fehlgeschlagenem Lesen oder Schreiben des Verlaufs liefert ausdrücklich `Playlist gestartet; Verlauf konnte nicht gespeichert werden: …`.

Gemeinsame Musik- und Spotify-Commands protokollieren jeden Command-Ausgang einmal im [App-Journal](TAURI-NOTIFICATIONS.md). Ein Erfolg lautet „angefordert“: Spotify bestätigt eine API-Anfrage, die YouTube-Music-Bridge die Queue-Aufnahme. Erst spätere Providerdaten belegen den tatsächlichen Wiedergabezustand. Fehler enthalten die ursprüngliche Ursache; bei abgelehntem Befehl entsteht kein Erfolgseintrag. Journalprobleme verändern den Command-Ausgang nicht.

## Nachweise und offene Abnahme

- Zuerst fehlgeschlagene UI-Tests belegen fehlende Controls und nutzbare veraltete Musik-Snapshots. Ein zusätzlicher fehlgeschlagener Test belegt den Verlust der anfänglichen Schnellplaylist bei Neuordnung der Favoriten. Komponenten- und produktive Dashboard-Routentests prüfen die Korrekturen, URI-Reihenfolge/Deduplizierung, ausgewählte Playlist, Shuffle/Repeat, Unbekannt-/Fehlerzustände, Retry und deaktivierte Provider.
- Native Tauri-IPC-Tests verbinden echte Commands mit lokalem Spotify-HTTP, Shared Player, Overlay-HTTP und Settings-Dateien: Seek wird auf die tatsächliche Titeldauer begrenzt; Shuffle/Repeat verwenden die richtigen Query-Parameter; ein HTTP-403 erzeugt ausschließlich einen Fehler. Playlistverlauf und Favoriten überleben Neustart. Ein nach angenommenem HTTP-Befehl absichtlich beschädigtes Settings-Dokument belegt die explizite Teilerfolgsfehlermeldung und das Ausbleiben eines Erfolgseintrags.
- Der native YouTube-Music-Bridge-Test verbindet IPC mit dem tatsächlichen lokalen Bridge-HTTP: `playpause` erreicht die Queue, der Journaltext lautet „angefordert“, der gespeicherte Wiedergabezustand wird nicht aus dieser Queue-Antwort erfunden. Providerwechsel und Shutdown geben die Bridge frei.
- Vollständiger Rust-Workspace, 202 Frontend-Tests in 49 Dateien, Command-Vertragsprüfung, TypeScript und Produktionsbuild erfolgreich. Eine separate Browser-Testfixture mit simuliertem Backend bestätigt Darstellung, Shuffle/Repeat, Playlistauswahl, Startfehler und erfolgreichen Retry; sie verwendet keine echten Konten oder Wiedergabe.

DA2 bleibt teilweise: Weitere C#-Meldestellen werden mit den jeweiligen Abläufen abgeglichen; OBS4/OBS6, AL1/AL2 und die installierte Gesamtabnahme bleiben gemäß [Umsetzungsplan](TAURI-IMPLEMENTATION-PLAN.md) offen. Der Browsernachweis und lokale HTTP-Fixtures ersetzen weder Spotify-Rechte/Premium/Geräte noch die praktische Windows-/macOS-Abnahme.
