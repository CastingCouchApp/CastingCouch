# Creator Control Suite 8.0.0-alpha59

## Build-Stabilisierung II

- Zwei ungültige Escape-Sequenzen in TimeSpan-Formatangaben korrigiert.
- Mehrzeilige Remote-Update-Statusausgabe auf `string.Join(Environment.NewLine, ...)` umgestellt.
- Nicht abgeschlossenes Zeichenfolgenliteral und dadurch ausgelöste Folgefehler beseitigt.
- Keine neuen Funktionen; diese Version konzentriert sich ausschließlich auf die Kompilierbarkeit von `CreatorControlSuite.App`.
