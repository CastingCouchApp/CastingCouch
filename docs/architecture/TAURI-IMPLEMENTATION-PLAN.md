# Tauri: Arbeitsziel und verbleibende Umsetzung

Stand: 3. Oktober 2026, einschließlich M1, Streamstart, Vorprüfung, nativem App-Journal, Szenen-/Audio-/Raid-/Profil-Schnellzugriffen, Dienste-Karte und gemeinsamen Musik-Schnellaktionen. Dieser Plan ersetzt die bisherige unspezifische Arbeitsanweisung „Stelle die App umschreibung fertig!!“. Er priorisiert die Arbeit, ohne den ausgewählten Funktionsumfang zu verkleinern.

## Verbindliches Gesamtziel

Die ausgewählten C#-Funktionen in Tauri auf Windows und macOS vollständig bedienbar machen: vorhandene Daten kompatibel übernehmen, OBS/Twitch/Musik verbinden, Overlay/Chat/Alerts im Stream verwenden, Unterbrechungen und Neustart beherrschen und Installation einschließlich Updateabschluss nachweisen. Jede verbleibende Funktionslücke erhält einen abgegrenzten Nutzerablauf, eine C#-Referenz und überprüfbare Abschlusskriterien. Erst nach dokumentierter Betriebsabnahme ist der Wechsel von WPF zulässig.

Workflow/Automatisierungsmodul, externe Steuerung/Multi-PC und kommerzielle Lizenzierung bleiben ausgeschlossen. Musikautomationen, lokale IPC, erforderliche interne OBS-Operationen und verlustfreie historische Einstellungen bleiben erhalten. Keine neue Funktion wird allein aus einem alten Dokumentationsversprechen abgeleitet.

## Was bereits vorhanden ist

Die [Feature-Matrix](TAURI-FEATURE-PARITY.md) enthält 53 ausgewählte Pakete: 37 stehen auf „Implementiert“, 13 auf „Teilweise“, drei auf „Offen“. Das ist eine Bestandsaufnahme der dokumentierten Implementierung, kein Prozentsatz abgeschlossener Migration. Bei einigen teilweise markierten Paketen fehlt vor allem der praktische Nachweis.

- B1–B7 sind implementiert und automatisiert geprüft. Die Basis erneut allgemein umzubauen ist kein nächster Meilenstein. Konkrete Regressionen werden weiterhin behoben; installierte Betriebsabnahme bleibt offen.
- Canvas-Verwaltung, Assets, Extension Packs, Chat, Countdown, große Teile von OBS/Twitch/Musik, Alert-Runtime, Profile, Dashboard und Sessionanalyse sind vorhanden.
- M1 bindet die native Streamende-/Raid-Steuerung als Dashboard-Karte und OBS-Stoppdialog ein. Manuelle Raid-Wege teilen Sperren und Kanalprüfung; App-Beenden wartet auf tatsächliche Mutationen und Cleanup und bleibt bei unaufgelöstem Raid geöffnet.
- Die zuletzt ausgeführten lokalen Rust- und Frontend-Prüfungen, Typprüfung und Produktionsbuild sind erfolgreich; 202 Frontend-Tests in 49 Dateien. Der native Raid-Test verbindet Tauri-IPC, Helix-HTTP, EventSub- und OBS-WebSocket einschließlich Wiederverbindung. Journal und Schnellzugriffe verbinden produktive Commands, Dateipersistenz, native Ereignisse und Dashboard-Karten. Musik-Schnellaktionen verwenden gemeinsame Spotify-HTTP-/Player-/Overlay-Verträge einschließlich Fehlern und Teilerfolg bei fehlgeschlagener Verlaufsspeicherung. Diese Prüfungen ersetzen weder echte Konten und Dienste noch die Installation auf beiden Zielplattformen.
- CI für `df4a8cc` (Schnellzugriffskarten, vor der Dienste-Karte) ist ebenfalls erfolgreich: [Build mit Windows/macOS-Tauri, WPF und Qualitätsprüfungen](https://github.com/CastingCouchApp/CastingCouch/actions/runs/37129300114) sowie [CodeQL](https://github.com/CastingCouchApp/CastingCouch/actions/runs/37129300090). Daraus folgt kein Nachweis eines installierten Streamablaufs.
- Windows-CI für `03701b2` scheiterte beim ersten Statistik-Writer-Ereignis am Ein-Sekunden-Testbudget; der Ablauf umfasst reale Settings-/Statistik-Dateioperationen einschließlich `sync_all`. Die Abonnements werden vor dem HTTP-Aufruf erstellt. Der Integrationstest verwendet nun ein begrenztes Fünf-Sekunden-I/O-Budget auch für die zuvor unbegrenzten Empfangs-/Shutdown-Waits und prüft zusätzlich den Writer-Fehlerzustand. Daten-, HTTP-, Hörzeit- und Neustartassertionen bleiben erhalten. Zehn lokale Wiederholungen erfolgreich; erneuter CI-Nachweis separat erforderlich.

## Verbleibende Arbeit nach Art der Lücke

| Bereich / IDs | Tatsächlich verbleibende Arbeit |
|---|---|
| Bedienpult DA2 | Streamende/Raid, bestätigter Streamstart, Vorprüfung, App-Journal sowie Szenen-, Audio-, Raid-Ziel- und Profil-Schnellzugriffe sind implementiert und automatisiert geprüft. [Dienst-Schnellstarts/-öffnen](TAURI-DASHBOARD-SERVICES.md) und [gemeinsame Musik-Schnellaktionen](TAURI-DASHBOARD-MUSIC.md) sind ebenfalls implementiert. Weitere C#-Meldestellen mit den jeweiligen Abläufen abgleichen. Live-/Installationsabnahme bleibt offen. |
| OBS4 / OBS6 | Medien-Restart/-Stopp und Browser-Refresh abgleichen; Monitoring um die tatsächlich vorhandenen C#-Werte und Fehlerabläufe vervollständigen. Allgemeine quellentypspezifische oder Filterparameter-Editoren sind in der geprüften C#-Oberfläche nicht belegt und werden ohne Referenz nicht zusätzlich vorausgesetzt. |
| AL1 / AL2 | Gespeicherte Audioausgangsauswahl, Text-/Medien-/Ausschnittsvorschau und tatsächliches OBS-Layout/Playback abgleichen; Abbruch und Cleanup gemeinsam mit Musik prüfen. C#-OBS-Renderer nutzt weder SoundPath noch Animation; die lokale WPF-Vorschau nutzt den gespeicherten Gerätewert ebenfalls nicht. Diese dort unvollständigen Funktionen gesondert dokumentieren. |
| SYS1 / SYS2 / SYS4 | Ersteinrichtung, Dokumentanzeige/versionierte Zustimmung, Migration mit Vorschau/Backup sowie vollständige Wiederherstellung. Settings-Roundtrip und App-Profile ersetzen kein Datenbackup. |
| SYS5 / SYS6 / SYS7 | Diagnoseoberfläche, Logfilter/API-Inspektor/Readiness; Abschluss des Updates statt bloßem Installerstart; fehlende Desktop-Optionen, Branding und durchgängige Anpassung. Autostart, Tray und TitleBar-Karten sind derzeit ausdrücklich als nicht verfügbar angezeigt. |
| O2 / O5 / OBS3 / OBS5 / TW3 | Canvas-/Widget-Verhalten, OBS-Browserquellen, Organisations-/Audiofunktionen und dauerhaftes Webchat-Login mit den tatsächlichen C#-Abläufen vergleichen und praktisch prüfen. Diese Einträge dürfen nicht pauschal als fehlende Backend-Implementierung behandelt werden. |
| Sämtliche übernommenen Pakete | Installierte Windows-/macOS-Abnahme einschließlich echter Verbindungen, Fehler/Wiederanlauf, Persistenz und Gesamtablauf. Erfolgreiches CI-Packaging allein ist kein Installationsnachweis. |

## Meilenstein M1: Streamende und Raid vollständig bedienbar

**Arbeitsziel:** Den bereits begonnenen Streamende-/Raid-Ablauf vom Dashboard und vom OBS-Stoppbutton bis zum tatsächlichen Abschluss oder Abbruch verbinden und über die nativen Grenzen absichern. In diesem Meilenstein werden keine anderen Feature-Pakete begonnen.

| Schritt | Konkretes Ergebnis | Abschlussnachweis |
|---|---|---|
| M1.1 Oberfläche | `StreamEndPanel` im Dashboard-Katalog und als OBS-Stoppdialog; gemeinsame native Laufzeit, erhaltene Layout-/Settings-Werte. | Route-/Interaktionstest: Stopp öffnet den Assistenten ohne vorherigen OBS-Stopp; alle Modi, Planung und tatsächlicher Abbruchabschluss bedienbar. |
| M1.2 Raid-Zustand | Direkte und generische Raid-Commands verwenden dieselbe Sperre und denselben Pending-Zustand. Ein Kanalwechsel darf keinen unaufgelösten Raid eines anderen Broadcasters abbrechen oder versehentlich freigeben. | Native IPC-Tests für aktive/unklare Raids, Kanalwechsel, fehlerhaften Abbruch, erfolgreiche Auflösung und parallele Aktionen. Die vorhandenen, bislang ungenutzten Host-Prüfungen werden eingebunden oder durch die geprüfte Lösung ersetzt. |
| M1.3 Dienstgrenzen | Durchgehender Ablauf mit nativen Commands, Helix-HTTP, EventSub-WebSocket und OBS-WebSocket. Ausgehende Bestätigung erreicht die normale Event-Bridge. | Falsche, eingehende und alte Raids stoppen den Stream nicht; Countdown null und Chatbefehl sind kein Abschlussnachweis; richtiger ausgehender Raid löst genau den gewählten Stopp aus. Fehlende/abgelehnte Subscription und Verbindungsabbruch bleiben sichtbar. |
| M1.4 Musik und Lebenszyklus | End-/Startszene und Musik werden einmal ausgeführt; externer OBS-Stopp, Wiederverbindung und App-Beenden während einer Mutation sind definiert. | Verspätete Szenenereignisse, Pause-/Stoppfehler und Abbruch während Raid-POST prüfen. Cleanup wartet auf das tatsächlich abgeschlossene Ergebnis; ein abgebrochener Ablauf stoppt keinen weiterlaufenden Stream. |
| M1.5 Abschluss | Dokumentierte Bedienung und verbleibende praktische Abnahme; getesteter Commit auf `main`. | Betroffene Contract-/Integration-/UI-Tests, vollständige passende Regression und Produktionsbuild erfolgreich; Browserprüfung des eingebundenen Dialogs. Live-/Installationsnachweis separat offen lassen, wenn kein geeigneter Dienst oder Zielrechner verfügbar ist. |

M1 gilt als **Implementierung abgeschlossen**, wenn M1.1–M1.5 erfüllt sind. DA2 und das Gesamtziel bleiben offen, solange weitere Bedienpultfunktionen oder die geforderte Betriebsabnahme fehlen. Ein isoliert grüner Komponenten- oder Actor-Test genügt nicht.

**Ergebnis:** M1.1–M1.5 sind implementiert und lokal geprüft. Dashboard-Einbindung, OBS-Stoppdialog, Legacy-Planungsdauer und fehlender Runtime-Status sind durch UI-Tests abgesichert. Native IPC prüft gemeinsame Raid-Sperren und Kanalwechsel einschließlich HTTP-Fehler und erfolgreicher Auflösung. Der durchgehende native Raid-Test prüft abgelehnte/erneuerte Subscriptions, verworfene falsche/eingehende/alte Bestätigungen und genau einen OBS-Stopp nach Wiederverbindung. Musiktests sichern verspätete Szenenereignisse und externen Stopp nach fehlgeschlagener verwalteter Operation ab; Shutdown wartet auf einen laufenden Raid-POST und tatsächlichen Abbruch. Der Dialog wurde zusätzlich im Browser mit einer ausdrücklich getrennten UI-Testfixture geprüft: Öffnen ohne Stoppbefehl, Moduswahl, scrollbare Bedienung, Start und Schließen nach Abbruchabschluss. Diese Fixture belegt Darstellung und UI-Verhalten, keine Dienstverbindung. Details: [Streamende-Vertrag](TAURI-STREAM-END.md).

**Nächste Arbeit:** M2 enthält bestätigten Streamstart einschließlich Startszene, native Vorprüfung, [App-Benachrichtigungsjournal](TAURI-NOTIFICATIONS.md), [Szenen-/Audio-/Raid-/Profil-Schnellzugriffe](TAURI-DASHBOARD-SHORTCUTS.md), [Dienste-Karte](TAURI-DASHBOARD-SERVICES.md) und [gemeinsame Musikaktionen](TAURI-DASHBOARD-MUSIC.md). Jetzt OBS4/OBS6- und AL1/AL2-Restliste abarbeiten; weitere C#-Meldestellen mit den betroffenen Abläufen abgleichen. [Streamstart und C#-Restreferenzen](TAURI-STREAM-START.md) trennen belegte C#-Bedienung von dort ebenfalls unvollständigen Quellen-/Alert-Funktionen. M1-Live-/Installationsabnahme bleibt als eigener Nachweis offen.

## Folgende Meilensteine

| Reihenfolge | Umfang | Abschlusskriterium |
|---|---|---|
| M2 Streamablauf und verbleibende Mediensteuerung | DA2-Rest, OBS4/OBS6, AL1/AL2 | Belegte C#-Restliste abgearbeitet; durchgehend bedienbarer Streamstart/-ende mit Quellen, Audio, Alert-Vorschau/-Wiedergabe und Fehlerbehandlung. In C# unvollständige Funktionen sind ausdrücklich dokumentiert. Bereits implementierte Szenenmusik und Monitoring bleiben konsistent. |
| M3 Einrichtung und Datensicherheit | SYS1, SYS2, SYS4 sowie benötigte SYS5-Diagnostik | Neue Installation einrichten; vorhandene Installation mit Vorschau und Backup übernehmen; fehlende/neue OAuth-Zugangsdaten sichtbar behandeln; Sicherung wiederherstellen. Rechtstexte/versionierte Zustimmungen ohne kommerzielle Lizenzierung. |
| M4 Verbleibende Anpassung und Kompatibilität | SYS5/SYS7-Rest, O2/O5, OBS3/OBS5, TW3 | C#-Vergleich konkretisieren und verbleibende Unterschiede schließen; unterstützte Themes/Branding, Desktop-Optionen, Canvas-Varianten, Audio/Quellen und persistentes Webchat-Login prüfen. |
| M5 Betrieb und Update | SYS6 und Abnahme aller ausgewählten Pakete | Windows/macOS ohne Repository: Migration → Dienste → OBS-Overlay → Stream mit Musik/Chat/Alerts → Unterbrechung/Wiederverbindung → Ende → Neustart → vollständig installiertes Update. Belege pro Plattform, Paketversion und geprüftem Commit. |

Praktische Prüfungen dürfen früher stattfinden, sobald ein Ablauf implementiert ist. Gefundene reale Fehler werden vor dem nächsten Paket behoben. M5 ist die abschließende Gesamtabnahme, kein Grund, sämtliche Live-Prüfungen bis zuletzt aufzuschieben.

## Regeln für zielgerichtete Fortsetzung

1. Jeweils einen Meilenstein und darin einen konkreten Nutzerablauf bearbeiten. Vor der Implementierung fehlgeschlagenen Test oder reproduzierbare Abweichung zur C#-Referenz festhalten.
2. Backend, UI, Persistenz und Ereignisfluss dieses Ablaufs gemeinsam fertigstellen. Unbenutzte Commands oder nicht montierte Komponenten gelten als Zwischenstand.
3. Implementierung, automatisierte Prüfung und installierte Betriebsabnahme getrennt führen. Fehlende Konten/Hardware oder ein nicht verfügbarer macOS-Rechner konkret als ausstehenden Nachweis dokumentieren.
4. Bereits erfolgreiche vollständige Prüfungen nur nach relevanten Änderungen oder neuen Fehlern wiederholen. Dokumentationsänderungen benötigen Link-/Konsistenzprüfung, keinen neuen Vollbuild.
5. Nach einem abgeschlossenen Abschnitt Statusmatrix und Vertragsdokument aktualisieren; auf Nutzeranweisung committen/pushen. Keine Versionserhöhung, Release-Tags oder WPF-Abschaltung aus einem Zwischenabschluss ableiten.

## Verbindliche Referenzen

- [Gesamter ausgewählter Umfang und Paketstatus](TAURI-FEATURE-PARITY.md)
- [Basisimplementierung und praktische Abnahme](TAURI-BASE-ACCEPTANCE.md)
- [Dashboard-Verträge](TAURI-DASHBOARD.md)
- [Streamende-/Raid-Verträge und konkrete offene Integration](TAURI-STREAM-END.md)

Die Abschnitte „Fortsetzung“ in der Feature-Matrix und alte Phasenprompts sind historische Arbeitsnachweise. Für die nächste Arbeit gelten dieser Plan und die aktuelle Matrix am Anfang des Dokuments.
