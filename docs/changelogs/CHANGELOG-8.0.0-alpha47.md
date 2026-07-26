# Creator Control Suite 8.0.0-alpha47

## Spotify-Zustandsverlauf – dauerhafte lokale Speicherung

- Verlaufseinträge bleiben nach einem Neustart erhalten.
- Favoriten und persönliche Notizen werden lokal gespeichert und wiederhergestellt.
- Statistikzähler für Speichern, Wiederherstellen, Verwerfen und Bereinigen bleiben erhalten.
- Suchtext, Aktionsfilter, Sortierung und der Filter „Nur Favoriten“ werden beim nächsten Start wiederhergestellt.
- Speicherung erfolgt atomar über eine temporäre Datei im lokalen Creator-Control-Suite-Datenordner.
- Beschädigte oder nicht unterstützte Persistenzdateien verhindern den Programmstart nicht und werden in der Automationsdiagnose protokolliert.
- Maximal 100 Verlaufseinträge werden dauerhaft vorgehalten.
