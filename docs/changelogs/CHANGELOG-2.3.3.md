# Creator Control Suite 2.3.3

## Build-Fix

- Behebt CS8602 in `RefreshAlertLibrary`.
- `AlertTypeBox` und `AlertLibraryList` werden vor der Verwendung auf `null` geprüft.
- Verhindert zugleich einen möglichen Startabsturz, wenn Ereignisse bereits während `InitializeComponent()` ausgelöst werden.
- Die Spotify-Alert-Mute-Funktion aus 2.3.2 bleibt unverändert enthalten.
