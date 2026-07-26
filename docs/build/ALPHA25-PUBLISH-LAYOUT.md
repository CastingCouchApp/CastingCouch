# 2.0.81 – Publish-Layout

Der echte Alpha-24-Publish war erfolgreich. Die Prüfung war falsch.

Das App-Projekt trägt den AssemblyName `CreatorControlSuite`. Deshalb heißen
die veröffentlichten Hauptdateien nicht `CreatorControlSuite.App.*`, sondern
`CreatorControlSuite.*`.

Außerdem wird zwischen zwei Zuständen unterschieden:

1. App-Publish nach Schritt 4/5
2. vollständiges Release-Publish nach CommandClient- und Updater-Publish

Erst der zweite Zustand ist die Voraussetzung für den Installer-Build.
