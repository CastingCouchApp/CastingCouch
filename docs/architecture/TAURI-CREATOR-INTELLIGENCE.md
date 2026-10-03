# Creator Intelligence in Tauri (DA4)

Stand: 3. Oktober 2026. Auf Implementierungsebene portiert; installierte Windows-/macOS- und Live-Abnahme bleibt offen. WPF bleibt verfügbar. Das Gesamtziel der Feature-Parität ist nicht abgeschlossen.

## Übernommenes Verhalten

Referenz sind `CreatorIntelligenceModels.cs`, `CreatorIntelligenceService.cs`, `CreatorIntelligenceService.Analysis.cs` und die vorhandene WPF-Bedienung unter `Shell/Services/CreatorIntelligence/`.

- Letzte Sitzung einschließlich laufender Streams: Dauer, Viewer-Durchschnitt/Peak, Bindung zwischen erstem und letztem Sample-Drittel, Chat/Follower pro Stunde, Szenen/Titel und Empfehlungen.
- Creator Score mit den ursprünglichen Gewichtungen und .NET-Mittelpunktrundung; Trendvergleich der letzten fünf mit bis zu fünf vorherigen Sitzungen, Qualitäts-/Engagement-/Wachstumsindex, Wochenwerte, stärkste Startzeit/Kategorie und bestehende Prognosen.
- Abgeschlossene Streams nach Zeitraum, bis zu zwölf neueste Sessions; Szenen-/Musiksegmente, Zeitfenster nach lokalem Wochentag und Stunde.
- Ereigniskorrelation nach fünf/zehn Minuten sowie Raid-Bindung nach fünf/zehn/dreißig Minuten. Richtungsgebundene Messpunktwahl mit höchstens zwölf Minuten Abstand; bisherige Rückfälle bei fehlenden Raid-Messpunkten bleiben erhalten.
- Maßnahmenvorschläge aus den letzten dreißig Tagen, manuelles Abschließen, automatische Zielerkennung und Wirkungsauswertung.
- Messbare Maßnahmen starten Experimente über die nächsten drei vollständigen Streams. Vergleich mit bis zu drei vorherigen Streams; vorhandene Status-, Vertrauens- und Bewertungsregeln.
- Notizen zur aktiven Sitzung mit Szene/Zuschauerzahl; HTML-Wochenbericht aus den letzten sieben Tagen und Öffnen der Daten bzw. des Berichts.

Die React-Bedienung liegt im Dashboard. Native Änderungen verwenden dieselben `stream-history-changed`-Ereignisse wie die Sitzungsverlaufansicht; eine regelmäßige Abfrage stellt verpasste Ereignisse wieder her. Fachlogik und Persistenz liegen in `ccs-modules::creator_intelligence` und dem gemeinsamen `StreamHistoryRuntime`.

## Daten und Fehlerverhalten

Das vollständige Journal `CreatorIntelligence/**/events.jsonl` ist die Berechnungsgrundlage. Die 500-Ereignisgrenze des sichtbaren Verlaufs beschränkt keine Auswertung. Lesen ist mit dem gemeinsamen Writer serialisiert, damit keine teilweise angehängten Zeilen als defekt gewertet werden. Ungültige Zeilen werden mit Hinweis übersprungen; die Quelldateien bleiben unverändert. Dateizugriffsfehler brechen die Abfrage sichtbar ab.

`action-plan.json` und `experiments.json` behalten C#-PascalCase, Kennungen, Zeitangaben und unbekannte Zusatzfelder. Änderungen lesen unter einer gemeinsamen Sperre frisch und speichern atomar. Beschädigte oder unbekannte Dateien werden nicht durch leere Listen überschrieben. Andere Analysebereiche bleiben verfügbar, wenn nur eine dieser Dateien betroffen ist.

Notizen verwenden eine persistierte Anfragekennung. Schreibfehler, Wiederholungen und Neustarts erzeugen keine zweite Journalzeile für dieselbe Notiz. Abgelehnte oder während einer Speicherung neu bearbeitete Entwürfe bleiben in der UI erhalten. Ohne aktive Sitzung meldet das Backend einen Fehler.

Gezielte Integrationskorrekturen gegenüber C#:

- Musik akzeptiert `track`, `name` und `title`; C# und der native Writer speichern tatsächlich `title`. Dadurch bekommen neue Musiksegmente einen nutzbaren Namen.
- Automatisch erreichte Maßnahmen behalten den ersten Abschlusszeitpunkt, statt ihn bei jeder periodischen Abfrage erneut zu setzen.
- Jeder Wochenbericht erhält einen eindeutigen Dateinamen. Mehrere Berichte innerhalb derselben Minute überschreiben sich nicht. Nutzertexte werden HTML-escaped; Datenhinweise sind im Bericht enthalten.
- Nicht gefundene Maßnahmen, manuelle Maßnahmen ohne messbare Experimentkennzahl und Speicherfehler werden sichtbar gemeldet.

## Nachweise und verbleibende Abnahme

`build/Generate-CreatorIntelligenceFixtures.ps1` kompiliert die unveränderten originalen C#-Modelle und Services mit einem isolierten Referenzprogramm. Dieses startet keinen Writer und liest keine Nutzerdaten. Die erzeugte Fixture umfasst 506 Ereignisse, zwölf abgeschlossene Sessions, eine laufende Sitzung, Maßnahmen und Experimente. Referenzwerte enthalten den Erzeugungszeitpunkt und lokalen Offset, damit die Rust-Vergleiche zeitlich reproduzierbar bleiben. Die Fixture kann ausdrücklich neu erzeugt werden; sie ist kein automatisch aktualisierter Erwartungswert.

- Rust-Vergleich von Sessionanalyse, Dashboard, Content, Korrelation, Maßnahmen, Wirkung und Experimenten mit den tatsächlichen C#-Ergebnissen.
- Randfälle: fehlende Samples, laufende/kurze Sessions, Bindungsgrenzen, Rundung, lokaler Tageswechsel und exakte Messpunktgrenzen.
- Persistenz-/Fehlerprüfungen: vollständiges Journal, beschädigte Dateien, unbekannte Zusatzfelder, idempotente Notizen, Schreibfehler/Retry, parallele Änderungen und Wiederanlauf.
- Native Tauri-IPC verbindet die echte Erfassung mit Analyse, Notizen, Maßnahmen, Experimenten, Bericht und erneutem App-State nach Neustart.
- React-Tests prüfen Zeitraum, Datenanzeige, Bedienbefehle, Ereignisaktualisierung, Fehlermeldungen und Entwurferhalt.

Noch praktisch nachzuweisen:

- [ ] Installierte Windows-App mit bestehenden C#-Journals und tatsächlichen OBS-/Twitch-/Musikdaten.
- [ ] Installierte macOS-App einschließlich OS-Zeitzone/DST, Persistenz und Öffnen des HTML-Berichts.
- [ ] Mehrere reale Streams, Unterbrechung/Neustart und Experimentabschluss nach drei vollständigen Sessions.

Builds und automatisierte Tests ersetzen diese Betriebsabnahme nicht. Die ursprünglichen übrigen Feature-Pakete bleiben verbindlich.
