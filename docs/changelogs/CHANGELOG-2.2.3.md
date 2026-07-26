# Creator Control Suite 2.2.3

- Behebt den Startabsturz im Alert-Audio-Editor.
- `ValueChanged` des Trim-Sliders kann während `InitializeComponent()` ausgelöst werden.
- `UpdateAlertAudioTrimLabels()` prüft deshalb jetzt alle beteiligten XAML-Steuerelemente auf `null`, bevor auf sie zugegriffen wird.
