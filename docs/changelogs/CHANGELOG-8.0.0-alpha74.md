# Creator Control Suite 8.0.0-alpha74

## IPC-Lebenszyklus serialisiert

- `StartAsync` und `StopAsync` werden jetzt über eine gemeinsame asynchrone Sperre serialisiert.
- Parallele Startaufrufe können nicht mehr mehrere Named-Pipe-Annahmeschleifen erzeugen.
- Ein gleichzeitig eintreffender Stop kann keinen neu gestarteten Serverzustand mehr überschreiben.
- Wiederholte Stop-Aufrufe sind idempotent und erzeugen keine doppelten Zustandswechsel.
- Die Lebenszyklus-Sperre wird beim finalen Dispose sauber freigegeben.
