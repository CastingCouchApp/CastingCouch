# Creator Control Suite 8.0.0-alpha64

## Agent-Version automatisch synchronisiert

- Die Agent-Version ist nicht mehr als feste Zeichenfolge im Quellcode hinterlegt.
- Der Agent liest seine Produktversion jetzt direkt aus `AssemblyInformationalVersion`.
- Ein optionaler Build-Metadaten-Suffix hinter `+` wird für Status-, Pairing- und Kompatibilitätsangaben entfernt.
- Falls keine Informationsversion verfügbar ist, wird kontrolliert auf die Assembly-Version und zuletzt auf `unknown` zurückgefallen.
- Die zentrale Projektversion wurde auf `8.0.0-alpha64` angehoben.

Damit können Suite und Agent bei künftigen Releases nicht mehr durch eine vergessene manuelle Versionsänderung auseinanderlaufen.
