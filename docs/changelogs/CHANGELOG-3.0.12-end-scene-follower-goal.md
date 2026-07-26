# Creator Control Suite 3.0.12

- Unter **Dienste > Twitch > Streamende und Raid** gibt es jetzt den Eintrag **Follower-Ziel auf der Endszene**.
- Der Zielwert wird zusammen mit den Streamende-Einstellungen gespeichert.
- Der Wert wird direkt in `twitch.followerGoal` und `twitch.followerGoalState.target` der aktiven `overlay-data.json` geschrieben.
- Das bisherige Ziel-Feld unter „Stream-Ziele & Overlay-Content“ bleibt synchron, damit beide Stellen denselben Wert bearbeiten.
