# 2.0.81 – Testhost-Retry

Ein Testhost-Absturz ist von einem fehlgeschlagenen Test zu unterscheiden.

2.0.81 wiederholt deshalb nicht pauschal jeden fehlgeschlagenen Testlauf.
Ein zweiter Versuch erfolgt nur, wenn die Ausgabe einen Testhost-Absturz oder
einen dadurch abgebrochenen Testlauf erkennen lässt.

Maximale Versuche: 2.

Vor dem zweiten Versuch werden nur Testhost-/VSTest-bezogene Prozesse
bereinigt. Normale fremde `dotnet`-Prozesse werden nicht beendet.

`--blame-crash` und `--blame-hang` bleiben für die Diagnose aktiv.
