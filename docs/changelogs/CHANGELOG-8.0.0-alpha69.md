# Creator Control Suite 8.0.0-alpha69

## Single-Instance-Aktivierung abgesichert

- Der IPC-Befehl `activate` blendet kein verstecktes Hauptfenster mehr ein.
- Während Rechtszustimmung oder Ersteinrichtung wird stattdessen nur das aktuell aktive beziehungsweise sichtbare Fenster aktiviert.
- Ist noch kein aktivierbares Fenster vorhanden, liefert der IPC-Befehl jetzt korrekt einen Fehler zurück. Die zweite Instanz kann dadurch weiter versuchen, die erste Instanz zu aktivieren, statt fälschlich Erfolg anzunehmen.
- Verhindert, dass eine zweite Programmausführung den vorgesehenen Startdialog umgeht.
