# Creator Control Suite 8.0.0-alpha75

## IPC-Laufzeit vom Aufrufer-Token entkoppelt

- `StartAsync` verwendet den CancellationToken des Aufrufers nur noch zum Abbrechen des Wartens auf die Lifecycle-Sperre.
- Die dauerhafte Named-Pipe-Annahmeschleife erhält einen eigenen internen CancellationToken.
- Ein später abgebrochener Startup-, Host- oder UI-Token kann den IPC-Server dadurch nicht mehr unbemerkt beenden.
- Vor einem Neustart werden veraltete abgeschlossene Lifecycle-Referenzen und ihre CancellationTokenSource bereinigt.
- Der sichtbare Running-Zustand kann dadurch nicht mehr von einer bereits beendeten Annahmeschleife abweichen.
