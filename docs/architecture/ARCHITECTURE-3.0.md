# Creator Control Suite 3.0 architecture migration

This alpha starts the migration without removing existing functionality.

## Completed in alpha 1

- Fixed the 2.9.8 compiler-blocking string literals in `MainWindow.xaml.cs`.
- Added the first `Core/Eventing` abstraction and thread-safe event bus.
- Added a neutral diagnostics result model for the upcoming startup checks.
- Updated the project version to `3.0.0-alpha.1`.

## Next migration steps

1. Introduce application composition and shared service registration.
2. Extract overlay import and central data handling from `MainWindow.xaml.cs`.
3. Add startup diagnostics for OBS, Spotify, Twitch, Streamer.bot, and overlays.
4. Move each navigation page into a dedicated View and ViewModel.
5. Replace polling-based UI refreshes with service events.

The existing UI remains intact during migration so each alpha can be tested independently.
