# 8.0.0-alpha102-hotfix18

- Scene-Automation: `rule.Shuffle` steuert wieder den Shuffle-Modus statt fälschlich die Startlautstärke.
- Timed Automation und RestorePrevious überschreiben per-Rule-/gespeicherten Shuffle nicht mehr mit dem globalen Setting.
- RestorePrevious startet den gespeicherten Track per Context-Offset und Seeket erst nach kurzer Aktivierungswartezeit.
- Live-Übergang setzt bei konfigurierter Live-Lautstärke tatsächlich `LiveVolumePercent` (statt nur Pause-Regeln zu überspringen).
- Transport und Volume nutzen das gespeicherte Preferred Device; Pause/Resume/Volume haben Token-Retry und patchen den lokalen Snapshot.
- Play/Pause-Toggle refresht den Playback-State vor der Entscheidung.
- Stream Deck/IPC: `spotify.toggle`, `spotify.volumeup`, `spotify.volumedown` und `spotify.playlist` sind verdrahtet; `value=` wird als Alias für `volume`/`scene`/`uri` akzeptiert.
- Stream-Deck-CMD-Generator schreibt kommandoabhängige Argument-Keys (`volume=`, `uri=`, `scene=`).
