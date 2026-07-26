# Creator Control Suite 2.0.166

- Zweiter Programmstart zeigt keine pauschale Meldung mehr an, sondern aktiviert die bereits laufende Hauptinstanz über die vorhandene Named-Pipe-IPC.
- Minimierte oder ausgeblendete Hauptfenster werden wieder angezeigt, normalisiert und in den Vordergrund geholt.
- Während die erste Instanz noch startet, versucht die zweite Instanz die Aktivierung mehrere Sekunden erneut.
- Der Single-Instance-Mutex wird nur noch von der Instanz freigegeben, die ihn tatsächlich besitzt.
- Falls die vorhandene Instanz wider Erwarten nicht erreichbar ist, wird eine konkrete Hilfestellung statt der irreführenden Standardmeldung angezeigt.
