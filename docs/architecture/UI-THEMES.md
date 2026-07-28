# UI-Themes

Die Creator Control Suite unterstützt austauschbare App-Shell-Themes (Farben + Fonts). Overlay-/Broadcast-Branding bleibt davon getrennt.

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

## Technik

- ResourceDictionaries unter `src/CreatorControlSuite.App/Themes/*.xaml`
- Live-Swap über `IThemeService` / `ThemeService` (`Application.Resources.MergedDictionaries`)
- Styles und Shell nutzen `{DynamicResource …}`-Tokens
- Katalog: `ThemeCatalog` in der App-Assembly

### Wichtige Token-Keys

Flächen: `WindowBackgroundBrush`, `PanelBackgroundBrush`, `SidebarBackgroundBrush`, `CardBackgroundBrush`, `ElevatedBackgroundBrush`, `InputBackgroundBrush`

Shell-Chrome: Custom Titlebar nutzt `SidebarBackgroundBrush`, `SidebarBorderBrush`, Caption-Hover über Nav-/Danger-Tokens

Text: `TextPrimaryBrush`, `TextSecondaryBrush`, `TextMutedBrush`, `TextOnAccentBrush`

Akzent/Status: `AccentColor`, `AccentBrush`, `AccentHoverBrush`, `SuccessBrush`, `WarningBrush`, `DangerBrush`

Nav: `NavHoverBackgroundBrush`, `NavActiveBackgroundBrush`, `NavActiveForegroundBrush`

Font: `AppFontFamily`, `AppHeadingFontFamily`

Legacy-Aliase (weiterhin gültig): `PanelBrush`, `CardBrush`, `MutedBrush`
