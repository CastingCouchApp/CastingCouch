# UI-Themes

Die CastingCouch unterstützt austauschbare App-Shell-Themes (Farben + Fonts). Overlay-/Broadcast-Branding bleibt davon getrennt.

## Auswahl und Persistenz

- UI: **Einstellungen → Allgemein → Darstellung → Theme**
- Setting: `General.ThemeId` in `%LocalAppData%\CreatorControlSuite\settings.json`
- Default / Fallback: `classic`
- Umschalten wirkt sofort (Preview); Speichern persistiert die Auswahl

## Verfügbarkeit

Alle Themes sind ohne Editionen oder Feature-Gates verfügbar. Unbekannte
Theme-IDs fallen weiterhin auf Classic zurück.

## Theme-Ids

| Id | Anzeigename |
|----|-------------|
| `classic` | Classic |
| `comic-sans-extravaganza` | Comic Sans Extravaganza |
| `pink-cage-flair` | Pink Cage Flair |
| `vespucci-heights` | Vespucci Heights |
| `vanilla-unicorn-lounge` | Vanilla Unicorn Lounge |
| `neon-night-market` | Neon Night Market |
| `terminal-green-override` | Terminal Green Override |
| `blood-moon-broadcast` | Blood Moon Broadcast |
| `pastel-lofi-cafe` | Pastel Lo-Fi Café |
| `gold-rush-studio` | Gold Rush Studio |
| `arctic-glass-lab` | Arctic Glass Lab |
| `biomilchs-bubatz-cantina` | biomilchs Bubatz Cantina |
| `fruppis-landadel-kanzlei` | fruppis Landadel Kanzlei |

## Technik

- ResourceDictionaries unter `src/CreatorControlSuite.App/Themes/*.xaml`
- Live-Swap über `IThemeService` / `ThemeService` (`Application.Resources.MergedDictionaries`)
- Styles und Shell nutzen `{DynamicResource …}`-Tokens
- Katalog: `ThemeCatalog` in der App-Assembly

### Wichtige Token-Keys

Flächen: `WindowBackgroundBrush`, `PanelBackgroundBrush`, `SidebarBackgroundBrush`, `CardBackgroundBrush`, `ElevatedBackgroundBrush`, `InputBackgroundBrush`

Shell-Chrome: Custom Titlebar nutzt `TitleBarBackgroundBrush` (Vertikal-Gradient für leichten 3D-Effekt), `TitleBarHighlightBrush` (obere Highlight-Kante), `TitleBarDividerBrush` (Widget-Trenner), `SidebarBorderBrush`; Caption-Hover über Nav-/Danger-Tokens. TitleBar-Widgets default flach (`TitleBarWidgetStyle` / `TitleBarDividerStyle`); optional Cards über `General.TitleBarWidgetCardsEnabled`. Einzelne Widgets per Rechtsklick-Menü ein-/ausblendbar (`General.TitleBarHiddenWidgets`, Keys in `TitleBarWidgetVisibility`).

Text: `TextPrimaryBrush`, `TextSecondaryBrush`, `TextMutedBrush`, `TextOnAccentBrush`

Akzent/Status: `AccentColor`, `AccentBrush`, `AccentHoverBrush`, `SuccessBrush`, `WarningBrush`, `DangerBrush`

Buttons: `ButtonHighlightBrush`, `ButtonPressedBrush` (halbtransparente Akzent-Overlays im Default-Button-Template; `OrangeActionButtonStyle` nutzt `AccentHoverBrush`/`AccentSelectedBrush`)

Auswahl: `ListBoxItem`-Selektion nutzt `AccentSelectedDeepBrush` + `AccentSelectedForegroundBrush` (statt `AccentSelectedBrush`/`TextOnAccentBrush`), damit Pastell-Themes lesbar bleiben. Sidebar-Nav setzt `FocusVisualStyle` auf `{x:Null}` (kein WPF-Punktestrich).

Nav: `NavHoverBackgroundBrush`, `NavActiveBackgroundBrush`, `NavActiveForegroundBrush` (aktive Sidebar-Items via `Tag=Active` + DynamicResource; Hover/Pressed wie Buttons über `ButtonHighlightBrush`/`ButtonPressedBrush`)

Font: `AppFontFamily`, `AppHeadingFontFamily`

Legacy-Aliase (weiterhin gültig): `PanelBrush`, `CardBrush`, `MutedBrush`
