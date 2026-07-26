# 2.0.81 – Build Fix 2

Der Alpha-18-Binlog zeigte den ersten verbleibenden Compilerfehler:

`AppIpcCommandRouter.cs`: `IFeatureGate` konnte nicht aufgelöst werden.

`IFeatureGate` liegt im Namespace `CreatorControlSuite.Core.Licensing`.
Der IPC-Router verwendete den Typ, importierte diesen Namespace jedoch nicht.

Zusätzlich schreibt `Invoke-NativeChecked` die vollständige native Ausgabe
jedes Schritts in ein separates TXT-Log. Dadurch ist der nächste Compiler-
oder Installerfehler ohne Binlog-Spezialauswertung direkt lesbar.
