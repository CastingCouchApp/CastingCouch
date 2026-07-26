# Creator Control Suite 2.0.132

## Dashboard ScrollViewer structure fix
- Fixed the three-zone dashboard XAML so the ScrollViewer has exactly one direct child.
- Added DashboardScrollRoot as the single ScrollViewer content root.
- Preserved DashboardLayoutGrid, the left/center/right zones, hidden module host, drag/drop behavior, module visibility and persisted layout state.
