# Creator Control Suite 8.0.0-alpha70

## Single-Instance Retry-Korrektur

- Die zweite Instanz wiederholt die Aktivierungsanfrage jetzt auch dann, wenn der IPC-Server bereits erreichbar ist, aber noch kein sichtbares Fenster aktivieren kann.
- Rechtszustimmung, Ersteinrichtung und verzögerter Hauptfensteraufbau können dadurch abgeschlossen werden, bevor auf den Prozessfenster-Fallback zurückgegriffen wird.
- Eine einzelne negative IPC-Antwort beendet die Wiederholungslogik nicht mehr vorzeitig.
- Produktversion auf `8.0.0-alpha70` aktualisiert.
