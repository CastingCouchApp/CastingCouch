# Creator Control Suite 2.0.97

## Persistent Notification Center
- Notification Center now distinguishes Info, Warning and Error messages.
- Added filters for all messages, info, warnings and errors.
- Unread notification count is displayed in the dashboard.
- Notifications can be marked as read.
- Notifications are persisted locally and restored after restarting the suite.
- Cache is limited to the latest 250 entries.
- Existing workflow, preflight, profile, stream-start, stream-end and raid status messages are routed into the central notification system where applicable.
- Notification persistence is deliberately non-critical: a corrupt cache cannot block application startup or streaming workflows.

Confirmation dialogs for destructive actions such as starting/stopping a stream are intentionally retained.
