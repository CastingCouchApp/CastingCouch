# Creator Control Suite 2.0.106

## Automatic live viewer sampling
- Added a dedicated timer that automatically reads the current Twitch viewer count while connected.
- Current viewer count is shown live in the dashboard instead of being overwritten by the historical average.
- Every successful viewer sample is fed into the existing workflow session statistics.
- Peak and average viewer statistics therefore build automatically from real Twitch live data.
- Current viewer count is also written into overlay-data through the existing overlay service.
- Viewer count resets to zero after the stream stops.
- Sampling is protected against overlapping API requests.
- Added configurable viewer sampling interval with a safe range of 5 to 300 seconds.
- Recommended interval is 15 to 30 seconds.
