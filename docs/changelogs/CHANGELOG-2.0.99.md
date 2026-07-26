# Creator Control Suite 2.0.99

## Command Center workflow rail
- Added a prominent Stream Command Center rail to the dashboard.
- Visual workflow stages: Vorbereiten, Bereit, Start, Live, Ende and Raid.
- Completed stages are shown separately from the currently active stage.
- Workflow summary text explains the current stream state.
- Added direct Prepare, Start and Stop actions to the command rail.
- Existing prepare/preflight, OBS start, live-scene transition, end-scene countdown and automatic raid workflows now update the visual stage indicator.
- The implementation uses the existing functional workflow methods rather than decorative-only dashboard buttons.
