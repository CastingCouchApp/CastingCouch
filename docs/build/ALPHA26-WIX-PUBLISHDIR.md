# 2.0.81 – WiX PublishDir

`-p:PublishDir=...` setzt eine MSBuild-Property.

Die WiX-Quelldateien verwenden dagegen:

`$(var.PublishDir)`

Damit diese Variable existiert, muss die MSBuild-Property über
`DefineConstants` an den WiX-Preprocessor weitergegeben werden:

`PublishDir=$(PublishDir)`

2.0.81 ergänzt diese Brücke im `.wixproj`.
