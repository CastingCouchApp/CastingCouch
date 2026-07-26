# Creator Control Suite 2.0.130

## Dashboard direct-drag repair
- Replaced fragile WPF DragDrop-based dashboard reordering with mouse-capture based live reordering.
- Dashboard modules now move visibly while the pointer is dragged in layout edit mode.
- Mouse-up finalizes the drag, persists the visual order, refreshes the layout editor, and saves settings.
- Lost mouse capture also finalizes safely so a drag cannot remain stuck.
- Existing drop handlers remain as a harmless fallback.
