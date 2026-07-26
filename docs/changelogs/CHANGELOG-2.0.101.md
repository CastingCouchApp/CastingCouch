# Creator Control Suite 2.0.101

## Dashboard Spotify playback progress
- Added a live playback progress bar to the dashboard Spotify player.
- Added elapsed and total track time display.
- Progress is updated from the existing Spotify playback snapshot.

## Spotify API rate-limit UX
- HTTP 429 no longer produces repeated modal popup dialogs.
- Rate-limit warnings are routed to the persistent Notification Center.
- Warning notifications are throttled to at most one per minute.
- The existing reduced-refresh behavior remains in place.
