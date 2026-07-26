# Creator Control Suite 8.0.0-alpha58

## Stabilisierung und Build-Fix

- Compilerfehler in `CreatorControlSuite.App/MainWindow.xaml.cs` behoben.
- Fehlerhafte Zeichenketten in der Stream-Deck-Mehrfachaktion korrigiert.
- Parameterwerte werden wieder korrekt in Anführungszeichen gesetzt und eingebettete Anführungszeichen escaped.
- Aufruf des CommandClient verwendet wieder korrekt maskierte leere Fenstertitel- und Programmpfad-Argumente.
- PowerShell-Wartebefehle für Schrittverzögerung und Cooldown verwenden wieder gültige C#-Zeichenketten.
- Keine neuen Funktionen hinzugefügt; Alpha 58 dient ausschließlich als stabilisierte Grundlage.

## Prüfung

- Alle XAML-Dateien erfolgreich als XML geparst.
- Betroffener C#-Fehlerbereich manuell und statisch geprüft.
- ZIP-Integrität erfolgreich geprüft.
- Ein vollständiger .NET-Build konnte in der Arbeitsumgebung nicht ausgeführt werden, da dort kein .NET SDK installiert ist.
