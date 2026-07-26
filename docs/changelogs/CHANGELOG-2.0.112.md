# Creator Control Suite 2.0.112

## Automatic Twitch follower tracking
- Added Twitch follower count retrieval through the Helix followers endpoint.
- Current total follower count is shown in the dashboard.
- Stream start stores the follower baseline automatically.
- New follower events refresh the current follower count immediately.
- Stream end performs a final follower refresh before history is saved.
- Followers gained during the stream are calculated automatically from baseline and current total.
- Follower totals and gained followers are synchronized to the existing workflow statistics and overlay data.
- The statistics and end-scene data therefore no longer depend on manually entered follower counts.
