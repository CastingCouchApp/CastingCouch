# Creator Control Suite 2.0.133

## WPF dashboard controls compile fix
- Added the missing System.Windows.Controls namespace to MainWindow.xaml.cs.
- Fixes unresolved StackPanel, Panel and ContextMenu types introduced by the three-zone dashboard layout.
- Also covers MenuItem and Separator used by the dashboard module context menus.
- Preserves the 2.0.132 ScrollViewer structure fix and all three-zone dashboard functionality.
