# UI-Themes

Die CastingCouch unterst├╝tzt austauschbare App-Shell-Themes (Farben + Fonts). Overlay-/Broadcast-Branding bleibt davon getrennt.

## Auswahl und Persistenz

- UI: **Einstellungen ÔåÆ Allgemein ÔåÆ Darstellung ÔåÆ Theme**
- Setting: `General.ThemeId` in `%LocalAppData%\CreatorControlSuite\settings.json`
- Default / Fallback: `classic`
- Umschalten wirkt sofort (Preview); Speichern persistiert die Auswahl

## Verf├╝gbarkeit

Alle Themes sind ohne Editionen oder Feature-Gates verf├╝gbar. Unbekannte
Theme-IDs fallen weiterhin auf Classic zur├╝ck.

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
| `pastel-lofi-cafe` | Pastel Lo-Fi Caf├® |
| `gold-rush-studio` | Gold Rush Studio |
| `arctic-glass-lab` | Arctic Glass Lab |
| `biomilchs-bubatz-cantina` | biomilchs Bubatz Cantina |

## Technik

- ResourceDictionaries unter `src/CreatorControlSuite.App/Themes/*.xaml`
- Live-Swap ├╝ber `IThemeService` / `ThemeService` (`Application.Resources.MergedDictionaries`)
- Styles und Shell nutzen `{DynamicResource ÔÇª}`-Tokens
- Katalog: `ThemeCatalog` in der App-Assembly

### Wichtige Token-Keys

Fl├ñchen: `WindowBackgroundBrush`, `PanelBackgroundBrush`, `SidebarBackgroundBrush`, `CardBackgroundBrush`, `ElevatedBackgroundBrush`, `InputBackgroundBrush`

Shell-Chrome: Custom Titlebar nutzt `TitleBarBackgroundBrush` (Vertikal-Gradient f├╝r leichten 3D-Effekt), `TitleBarHighlightBrush` (obere Highlight-Kante), `TitleBarDividerBrush` (Widget-Trenner), `SidebarBorderBrush`; Caption-Hover ├╝ber Nav-/Danger-Tokens. TitleBar-Widgets default flach (`TitleBarWidgetStyle` / `TitleBarDividerStyle`); optional Cards ├╝ber `General.TitleBarWidgetCardsEnabled`. Einzelne Widgets per Rechtsklick-Men├╝ ein-/ausblendbar (`General.TitleBarHiddenWidgets`, Keys in `TitleBarWidgetVisibility`).

Text: `TextPrimaryBrush`, `TextSecondaryBrush`, `TextMutedBrush`, `TextOnAccentBrush`

Akzent/Status: `AccentColor`, `AccentBrush`, `AccentHoverBrush`, `SuccessBrush`, `WarningBrush`, `DangerBrush`

Nav: `NavHoverBackgroundBrush`, `NavActiveBackgroundBrush`, `NavActiveForegroundBrush`

Font: `AppFontFamily`, `AppHeadingFontFamily`

Legacy-Aliase (weiterhin g├╝ltig): `PanelBrush`, `CardBrush`, `MutedBrush`
