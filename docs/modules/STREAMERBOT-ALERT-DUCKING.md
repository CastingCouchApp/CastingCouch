# Streamer.bot-Alerts an die Spotify-Absenkung melden

Die CastingCouch berücksichtigt jetzt eigene Alerts und Alerts aus Streamer.bot gemeinsam.

Füge in jede Streamer.bot-Alert-Aktion am Anfang eine **Execute Program**-Sub-Action ein:

```text
CreatorControlSuite.CommandClient.exe alert.external.start source=streamerbot id=%eventSource%-%userId%
```

Füge am Ende derselben Aktion eine zweite **Execute Program**-Sub-Action ein:

```text
CreatorControlSuite.CommandClient.exe alert.external.end source=streamerbot id=%eventSource%-%userId%
```

Wichtig: Start und Ende müssen dieselbe `id` verwenden. Für einfache, nicht überlappende Alerts kann stattdessen `id=default` verwendet werden.

Zum Zurücksetzen hängen gebliebener Streamer.bot-Alerts:

```text
CreatorControlSuite.CommandClient.exe alert.external.clear source=streamerbot
```

Die vorhandene Spotify-Einstellung **Musik während Alerts reduzieren** gilt damit für beide Alert-Quellen. Die ursprüngliche Lautstärke wird erst wiederhergestellt, wenn alle aktiven Alerts beendet sind.
