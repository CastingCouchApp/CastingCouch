# 2.0.81 – veraltete ComponentGroupRef entfernt

Nach der Umstellung von einer expliziten `PublishedApplicationFiles`
ComponentGroup auf WiX-`Files`-Harvesting blieb in `Package.wxs` noch eine
Referenz auf die nicht mehr vorhandene Gruppe zurück.

2.0.81 entfernt:

`ComponentGroupRef Id="PublishedApplicationFiles"`

Die Dateieinbindung erfolgt direkt über `DirectoryRef` und `Files`.
