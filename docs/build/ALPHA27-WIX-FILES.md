# 2.0.81 – WiX Files-Struktur

Der Alpha-26-Build hat bestätigt, dass `PublishDir` korrekt an WiX
weitergegeben wird.

Der nächste Fehler war rein strukturell:

`Component` enthielt ein nicht erlaubtes `Files`-Element.

2.0.81 verschiebt das Dateiharvesting auf die Ebene von
`DirectoryRef Id="INSTALLFOLDER"`.

Dadurch kann WiX die Inhalte aus `PublishDir` unterhalb des
Installationsverzeichnisses erzeugen.
