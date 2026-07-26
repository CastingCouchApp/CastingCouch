# 2.0.81 – Test-Hang-Diagnose

Der Alpha-20-Transcript endet unmittelbar nach `3/5 Tests`.
Der vorherige Buildschritt war mit 0 Warnungen und 0 Fehlern erfolgreich.

Da kein ExitCode zurückkam, wurde auch kein BuildFailure erzeugt. 2.0.81
kapselt den Testprozess deshalb separat.

Der .NET-Testhost erhält `--blame-hang --blame-hang-timeout 60s`.
Zusätzlich beendet die Build-Pipeline den gesamten Testprozess nach maximal
180 Sekunden. stdout und stderr werden getrennt in `artifacts\build-logs`
geschrieben und vom Diagnosepaket eingesammelt.
