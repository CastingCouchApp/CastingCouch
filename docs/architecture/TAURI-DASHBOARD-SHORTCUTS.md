# Dashboard: Szenen, Audio, Raid-Ziel und Schnellprofil

Stand: 2026-10-03. Die hier beschriebenen Schnellzugriffe sind implementiert und automatisiert geprüft. Dienst-Schnellstarts, weitere Journaleinträge und installierte Windows-/macOS-Abnahme bleiben DA2-Restumfang.

## C#-Referenz und Karten

Referenzen: `src/CreatorControlSuite.App/Views/Pages/Dashboard/DashboardPageView.xaml`, `Shell/Dashboard/MainWindow.Dashboard.Bindings.cs` und `MainWindow.Dashboard.Runtime.cs`; OBS-Audio in `Shell/Services/Obs/MainWindow.Services.Obs.ServiceControls.cs`; Raid-Ziel in `Shell/Services/Twitch/MainWindow.Services.Twitch.DashboardRaid.cs`.

Die gespeicherten Kartenschlüssel `Scenes`, `AudioMixer` und `RaidAssistant` werden jetzt im nativen Katalog und der produktiven Dashboard-Route verwendet. Bestehende Reihenfolge, ausgeblendete Karten, Spalten und Größen bleiben erhalten. Standardspalten wie C#: Szenen und Audio links, Raid-Assistent rechts. Die Karten gehören zu `ShowAdvancedTools`. Die allgemeinen Dienst-Schnellstarts aus `QuickServices` sind damit noch nicht übernommen.

## Szenen-Schnellwahl

Vier Buttons verwenden die konfigurierten Start-, Live-, Pause- und Endszenen. Sie bleiben unabhängig von bearbeiteten oder bewusst gelöschten individuellen Szenenbuttons verfügbar. Fehlende Konfiguration und fehlende OBS-Verbindung deaktivieren den betreffenden Zugriff. Der tatsächliche aktuelle Szenenname bestimmt `aria-pressed`; eine Anfrage setzt keinen vorzeitigen aktuellen OBS-Zustand.

Es wird der gemeinsame native `obs_set_scene`-Command verwendet. Ein Klick startet oder beendet keinen Stream. Erfolgreiche OBS-Antworten und Fehler erhalten Journaleinträge. Dashboard und Serviceseite verwenden dabei dieselbe Fachlogik und dieselbe OBS-Verbindung.

## OBS-Audiomixer

Die Quellenliste stammt aus `GetInputList` über den typisierten `obs_query`-Command. Für die gewählte Quelle werden `GetInputMute` und `GetInputVolume` abgefragt. Mute, Unmute und dB-Wert verwenden `obs_control` mit `set_mute` beziehungsweise `set_volume`, `inputName` und den camelCase-Feldern des generierten Vertrags. Es gibt keine zusätzliche Audio-Engine.

Mute und numerische Lautstärke müssen bekannt und die Abfragen fehlerfrei sein, bevor Änderungen angeboten werden. Quellen ohne Audioeigenschaften und unvollständige Antworten werden sichtbar behandelt. Eingaben sind auf den C#-Bereich −100 bis +26 dB begrenzt. Während einer laufenden Änderung bleiben weitere Mutationen dieser Quelle deaktiviert. Nach Erfolg oder Fehler werden die Audio- und Quellenansichten erneut abgefragt; ein erfolgloser Befehl wird nicht als neuer Mute-/Lautstärkestatus dargestellt.

Ein bearbeiteter dB-Entwurf bleibt bei Hintergrundabfragen erhalten. Ein Quellenwechsel verwendet getrennte Query-Keys und einen neuen Editor, sodass ein alter Entwurf nicht versehentlich auf die neue Quelle angewendet wird. Journaleinträge entstehen nach dem nativen Ergebnis, auch bei Bedienung auf der Serviceseite. Die ausgewählte Quelle ist wie im C#-Schnellmixer ein Laufzeitzustand, keine neue Einstellung.

## Raid-Ziel und App-Profil

Der Raid-Assistent liest dasselbe gespeicherte Ziel wie Streamende und Community-Seite. `twitch_raid_target` liefert Bild, Onlinezustand, Zuschauerzahl, Kategorie, Titel und Livebeginn aus der bestehenden nativen Helix-Integration. Eine manuelle Wiederholung und Hintergrundabfragen aktualisieren die Anzeige. Native Raid-Ereignisse invalidieren Zielauswahl und Zielabfrage; Listenerfehler sind sichtbar und erneut verbindbar.

Während einer Abfrage, nach fehlgeschlagenem Abruf oder bei getrenntem Twitch wird ein früherer Onlinezustand nicht als aktueller Nachweis angezeigt. Ein unbekannter Kanal ist ein eigener Leerzustand. Der Zugriff prüft das Ziel; die bestehenden Streamende-/Raid-Wege behalten ihre gemeinsamen Mutationssperren. Er startet keinen neuen parallelen Raid-Weg. Verwaltung ist über die Serviceseite erreichbar.

Das Schnellprofil verwendet `list_profiles`, den aktuellen vollständigen Settings-Snapshot und denselben `apply_profile`-Command wie die Profilverwaltung. Vor dem Anwenden wird auf das Ersetzen aktueller/ungespeicherter Einstellungen hingewiesen und bestätigt. Fehler, Konflikte und Warnungen bleiben sichtbar. Nach erfolgreicher Anwendung werden die aktiven Abfragen aktualisiert; der nächste Szenen-/Raid-/Musikzustand folgt den gespeicherten Einstellungen.

Profilname und vorbereitete Einstellungen stammen aus derselben nativen Profil-Dateilesung. Settings-Validierung, Konfliktprüfung, Server-Portwechsel und Wiederverbindung bleiben im gemeinsamen Speicherablauf. Erfolg, Fehler und Warnungen werden journalisiert. Der gestrichene Workflow-Aufruf „Vorbereiten“ wird weder ausgeführt noch als Erfolg behauptet. Verwaltung führt zur bestehenden Einstellungsseite mit App-Profilen.

## Nachweise und weitere Arbeit

Die zuerst fehlgeschlagenen Tests belegen die fehlenden Komponenten/Katalogeinträge und die bisher nicht registrierte Szenenoperation in der nativen IPC-Test-App. Der Produktionshost hatte diesen Command bereits; beide Handler führen jetzt dieselbe geprüfte Szenenfunktion aus.

- Rust-Dashboard-Test: Legacy-Kartenschlüssel, Reihenfolge, ausgeblendeter Audiomixer, Zonen und unveränderter Settings-Roundtrip; gemeinsamer Frontend-Standardvertrag.
- Native IPC mit tatsächlichem OBS-v5-WebSocket-Testserver: Quellen-/Lautstärkeabfrage, Szenenwechsel, Mute, Lautstärke, ursprüngliche OBS-Fehlerantworten, Journaleinträge und deren Neustartpersistenz. Kein Streamstart/-stopp durch die Schnellzugriffe.
- Bestehende native Profil-Tests: erfolgreicher gespeicherter Profilname im Journal, Konflikt als Fehlereintrag, unveränderte Settings nach fehlgeschlagenem Anwenden; Portkonflikt und kontrollierter Neustart bleiben abgesichert.
- Sechs UI-Tests: vier konfigurierte Szenen, Szenenfehler, echte Command-Argumente, erhaltener Audioentwurf, Quelle ohne/unvollständige Audioeigenschaften, veraltete Raid-Angaben nach Fehler, Zielwechsel über Ereignis, Profilbestätigung/-fehler/-erfolg und aktualisierter Settings-Cache.
- Produktiver Dashboard-Routentest: alle drei Karten sind montiert. Getrennte Browser-Testfixture: Szenenklick, Mic-Auswahl, −6 dB/Mute, Raid-Zielprüfung und Profilwahl sichtbar geprüft; keine realen Dienste.

Vollständiger Rust-Workspace, 189 Frontend-Tests in 47 Dateien, Command-Vertrag, TypeScript und Produktionsbuild sind erfolgreich. Eine erste vollständige UI-Prüfung scheiterte beim Laden einer lokalen Testabhängigkeit; Einzelwiederholung und anschließende vollständige Wiederholung bestanden ohne Dependency- oder Produktänderung.

[Dienst-Schnellstarts/-öffnen](TAURI-DASHBOARD-SERVICES.md) sind inzwischen ebenfalls implementiert. Nächster DA2-Abgleich: Musikaktionen und weitere Meldestellen. OBS4/OBS6 und AL1/AL2 bleiben eigene M2-Arbeiten. Reale Konten, Quellen und installierte Windows-/macOS-Nutzerabläufe sind gesondert nachzuweisen; WPF bleibt verfügbar.
