# Creator Control Suite 8.0.0-alpha102 Hotfix 2

## Build fixes

- Fixed invalid `.Value` access on a nullable viewer count fallback.
- Suppressed the already guarded nullable payload access in `BuildSummary`.
- Updated Spotify health recovery to the current `ActivatePreferredDeviceAsync(bool, CancellationToken)` signature.
- Replaced the unavailable `File.CopyAsync` call with an asynchronous `Task.Run` wrapper around `File.Copy`.
- Includes all corrections from alpha102-hotfix1.
