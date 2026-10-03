# Streamende und Raid in Tauri

Stand: 2026-10-03. Die native Ablaufsteuerung und der kompatible Einstellungsadapter sind implementiert. Die Anbindung an AppState, tatsächliche Dienstoperationen, Tauri-Commands und React-Bedienung ist noch offen. DA2 bleibt teilweise umgesetzt; der bisherige OBS-Stoppbutton verwendet weiterhin den direkten OBS-Befehl. Dieser Zwischenstand ist keine Betriebsabnahme des Assistenten.

## Referenz und implementierter Ablauf

Maßstab sind `MainWindow.Services.Obs.StreamEndExecution.cs`, `StreamEndPlanning.cs`, `StreamStart.cs`, die Twitch-Bindings sowie `StreamEndMode` und `RaidStartPolicy` im C#-Stack. Insbesondere wartet `RunRaidCountdownAsync` nach Countdown null auf das tatsächliche ausgehende Raid-Ereignis. Der ältere Helper `RaidCountdownOutcome` ersetzt dieses Verhalten nicht.

Die Fachlogik liegt in `tauri-app/src-tauri/crates/ccs-modules/src/stream_end.rs`:

- `Immediate` stoppt OBS mit bis zu drei Versuchen und einer Sekunde Abstand. Erst nach Erfolg folgen Startszene und optionales Musikpausieren. Fehlgeschlagener OBS-Stopp löst diese Folgeoperationen nicht aus.
- `EndSceneThenStop` aktiviert die Endszene, startet bei entsprechender Auswahl Endmusik und wartet den Endszene-Countdown ab. Ein Abbruch lässt den Stream weiterlaufen. Der Countdown kann ausdrücklich übersprungen werden.
- `EndSceneRaidThenStop` prüft das Raid-Ziel unmittelbar nach Aktivierung der Endszene. Der Raid wartet nicht zuerst deren Dauer ab. Nicht gefundene/offline Ziele werden alle fünf Sekunden erneut geprüft. Sichere vorübergehende Fehler verwenden fünf, acht, zwölf und danach fünfzehn Sekunden Wartezeit. Das Startbudget beträgt standardmäßig 120 Sekunden, begrenzt wie C# auf 15–600 Sekunden.
- Ein angenommener Raid startet den lokalen Countdown von 5–300 Sekunden. Nach dessen Ende bleibt die Steuerung im Bestätigungszustand. Ein Streamstopp nach Raid benötigt ein passendes tatsächliches Twitch-Ereignis und berücksichtigt `StopStreamAfterRaid`. Musikpausieren berücksichtigt die getrennte historische Option `StopSpotifyAfterRaid`.
- „Jetzt raiden“ ist nach zehn Sekunden verfügbar und fordert einen Chatbefehl an. Ein erfolgreich gesendeter Befehl ist keine Raid-Bestätigung.
- Raid abbrechen führt nach erfolgreichem Twitch-Abbruch zurück zur Zielprüfung innerhalb des ursprünglichen Startbudgets. Raid überspringen beendet den Stream erst nach erfolgreichem Abbruch eines bereits laufenden Raids. Schlägt dieser Abbruch fehl, bleibt der Bestätigungszustand mit sichtbarem Fehler erhalten.
- Ein geplantes Streamende verwendet dieselbe Steuerung; nur ein Ablauf kann gleichzeitig aktiv sein. Es kann vorzeitig gestartet oder abgebrochen werden. `RaidOnStreamEnd` bestimmt wie C# den automatischen Modus unabhängig von der normalen Dialogauswahl. Ein laufender Countdown wird nicht automatisch nach App-Neustart wieder aufgenommen.

Snapshots und Änderungsereignisse enthalten Lauf-ID, Phase, Status, Zeit, Versuch, Ziel, verfügbare Raid-Aktion, noch unaufgelösten Raid, Fehler und Warnungen. Fehler beim Musikpausieren oder beim Zurücksetzen der Szene werden als Warnung gemeldet; sie machen einen bereits erfolgreichen OBS-Stopp nicht rückgängig.

## Nebenläufigkeit und tatsächliche Bestätigung

Mutierende Operationen werden nicht durch das Fallenlassen ihrer Future abgebrochen. Ein während eines Raid-POST angeforderter Abbruch wartet dessen Ergebnis ab und bricht einen angenommenen oder unklaren Raid anschließend bei Twitch ab. Solange dies läuft, ist kein zweiter Streamende-Ablauf möglich. Scheitert der Twitch-Abbruch, bleibt der unaufgelöste Raid im Snapshot erhalten und blockiert einen neuen Ablauf.

Bestätigung prüft Absender und Ziel anhand der Twitch-IDs, ersatzweise anhand der Logins. Eine widersprechende vorhandene ID kann nicht durch einen passenden Login umgangen werden. Eingehende Raids, andere Ziele und Ereignisse vor der aktuellen Anfrage gelten nicht als Bestätigung. Der EventSub-Parser bewahrt jetzt zusätzlich `eventSubMessageTimestamp` in den bestehenden Eventdaten auf. Alte oder ungültige vorhandene Server-Zeitstempel werden verworfen; Empfangszeit allein verwandelt eine erneut zugestellte alte Nachricht nicht in einen neuen Nachweis.

Gültige Bestätigungen werden unabhängig von der begrenzten Steuerbefehlsqueue gespeichert. Deshalb gehen sie auch während langsamer I/O oder vieler wartender Bedienbefehle nicht verloren. Bei gleichzeitigem Abbruch und schon bestätigtem Raid wird dessen tatsächlicher Abschluss übernommen; die App stoppt den Stream nicht und sendet keinen neuen Raid-Abbruch für einen möglicherweise inzwischen anderen Raid.

Unklare Mutationsantworten bleiben ausdrücklich unaufgelöst. Netzwerkfehler, HTTP-5xx und andere nicht eindeutig abgelehnte Raid-Anfragen erzeugen keinen zweiten POST und führen auch nach Ablauf des Startbudgets nicht automatisch zum Streamstopp. Dies erhält die bereits vorhandene Schutzwirkung von `TwitchClient::community_start`, statt den früheren allgemeinen C#-Retry auf möglicherweise angenommene Mutationen zu übertragen.

## Einstellungen

Der Adapter liest C#-PascalCase-Felder und numerische oder benannte `StreamEndMode`-Enums. Eine unveränderte effektive Auswahl erhält die originale Repräsentation, unbekannte verschachtelte Felder und ältere Fallback-Werte. Erst bearbeitete Felder werden geschrieben. Eine bearbeitete Endszene-Dauer aktualisiert sowohl `Twitch.EndSceneDurationSeconds` als auch den intern benötigten historischen Fallback `Workflow.EndSceneSeconds`; daraus entsteht keine Workflow-Seite.

Ein bearbeitetes Raid-Ziel wird normalisiert und in den vorhandenen, ohne Groß-/Kleinschreibungsduplikate geführten Raid-Kanalverlauf aufgenommen. Bekannte beschädigte Werte oder unbekannte Modi werden als Fehler gemeldet. Lange gültige C#-Countdowns bleiben unterstützt; es wird keine zusätzliche Tagesgrenze eingeführt. Der Host muss diesen Adapter mit dem vorhandenen `JsonSettingsStore::save_edit` verbinden, damit parallele Seitenänderungen erhalten bleiben.

## Noch erforderliche Host- und UI-Anbindung

1. AppState-Runtime, typisierte Commands/Events, konfliktsicherer Einstellungsdialog, geplantes Streamende und Dashboard-Bedienung anbinden. Queued Abbruch darf nicht vor dem tatsächlichen Ergebnis als abgeschlossen angezeigt werden.
2. `StreamEndIo` über die vorhandenen OBS-, Twitch- und Musikclients ausführen. OBS-Stopp benötigt Wiederverbindung mit den vorhandenen Einstellungen und dem OS-Keyring. Sitzungserfassung muss weiterhin am tatsächlichen OBS-Ausgang hängen und die gesamte Endszene mitzählen.
3. EventSub um die ausgehende `channel.raid`-Subscription mit `from_broadcaster_user_id` ergänzen. Aktuell wird nur die eingehende Richtung abonniert. Verfügbarkeit und Fehler dieser Subscription müssen sichtbar sein. Ausgehende Bestätigungen dürfen weder eingehende Raid-Alerts noch `IncomingRaids`-Statistik auslösen.
4. Den konfigurierten Broadcaster eindeutig auflösen; das authentifizierte Benutzerkonto ist bei Moderation fremder Kanäle nicht zwangsläufig der Broadcaster. Manuelle Community-Raids und der Assistent müssen denselben Pending-Zustand verwenden. Nach fehlgeschlagenem Abbruch benötigt ein unaufgelöster Raid einen geprüften manuellen Auflösungspfad.
5. Vorhandene Szenenmusik und Streammusik einbinden, ohne Playlist oder Pause doppelt auszulösen. Konfigurierte Szenenregeln und deren Vorrang behalten; gespeicherte Spotify-/Legacy-Musikoptionen und den gemeinsamen ausgewählten Musikprovider berücksichtigen.
6. Native IPC-, tatsächliche OBS-WebSocket-/Helix-/EventSub-Vertragstests, UI-Abläufe und installierte Nutzerabläufe auf Windows/macOS nachweisen. Allgemeine Vorprüfungen, Benachrichtigungen und Schnellzugriffe aus DA2 bleiben ebenfalls erforderlich.

## Tests und Abnahmegrenze

Tests wurden vor der Implementierung und den konkreten Fehlerkorrekturen hinzugefügt. 29 Rust-Ablauftests verwenden kontrollierte I/O und virtuelle Tokio-Zeit für alle drei Modi, Countdown, Retry/Timeout, Abbruch während POST, gleichzeitig eintreffende Bestätigung, Fehlermeldungen, Optionen, lange C#-Werte, verlustfreie Einstellungen und Aktualisierungsereignisse. Ein zusätzlicher EventSub-Parsertest sichert den Server-Zeitstempel. Vollständiger Rust-Workspace und bestehender generierter Command-Vertrag geprüft.

Diese Tests belegen die Fachsteuerung. Sie belegen noch keine Anbindung der neuen Steuerung an echte Dienstclients oder die Bedienoberfläche. Das gesamte ausgewählte Paritätsziel und die installierte Betriebsabnahme bleiben offen; WPF bleibt verfügbar.
