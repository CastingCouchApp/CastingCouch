Lokale Private Keys für Entwicklung und manuelle Manifest-Signierung.

- update-private.pem
- license-private.pem

Nicht committen (*.pem ist gitignored).
CI nutzt das Secret UPDATE_SIGNING_KEY_PEM.
Erzeugen: ../Generate-DevelopmentKeys.ps1
