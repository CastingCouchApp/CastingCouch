# Architektur

Die aktuelle Ist- und Zielarchitektur der 8.x-Linie ist unter
[`ARCHITECTURE-8.0.md`](ARCHITECTURE-8.0.md) dokumentiert.

Verbindliche Leitplanken:

- WPF/.NET 10 bleibt die Desktop-Plattform.
- Die Anwendung bleibt ein modularer Monolith.
- Secrets liegen ausschließlich im DPAPI-geschützten `ISecretStore`.
- Views enthalten keine Geschäftslogik; die Shell verantwortet nur Navigation
  und Window-Lifecycle.
- Core referenziert keine App- oder Integrationsprojekte.
- Neue und extrahierte Quell-/XAML-Dateien bleiben unter 1.000 Zeilen.
- Änderungen an Geschäftslogik erfolgen testgetrieben.

Historische Migrationsnotizen bleiben in
[`ARCHITECTURE-3.0.md`](ARCHITECTURE-3.0.md) erhalten, sind aber nicht mehr das
aktuelle Zielbild.
