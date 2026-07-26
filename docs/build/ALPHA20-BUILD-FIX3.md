# 2.0.81 – Build Fix 3

Der Alpha-19-Build meldete 101 Fehler. Die Fehleranalyse zeigte wenige
wiederkehrende Ursachen:

1. `System.IO` fehlte im App-Projekt.
2. `Xunit` fehlte als globaler Test-Namespace.
3. `Application.MainWindow` ist statisch als `Window` typisiert.
4. Ein nullable Dokumentpfad wurde direkt dereferenziert.
5. Spotify- und Twitch-Module verlangen explizite CancellationTokens.

Die Imports wurden zentral über `GlobalUsings.cs` ergänzt. Dadurch werden
die vielen identischen `Path`-, `File`-, `Directory`- und `[Fact]`-Fehler
an der Ursache behoben.
