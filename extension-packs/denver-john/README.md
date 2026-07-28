# Denver John – Overlay Extension Pack

Port des Stream-Logos aus `logo.html` als Canvas-Widget.

## Inhalt

| Typ | Runtime-ID | Beschreibung |
|-----|------------|--------------|
| Widget | `ext:denver-john:logo` | Animiertes DJ-Monogramm + „DENVER JOHN“ mit Shine |

## Installation

1. ZIP aus dem Pack-Ordner bauen (oder `denver-john.zip` nutzen).
2. In der App: Overlay → Extension Packs → ZIP importieren.
3. Alternativ Dev: Ordner nach `CanvasOverlay/dev/extensions/denver-john/` kopieren und `make canvas-dev` starten.

## Props

| Prop | Default | Bedeutung |
|------|---------|-----------|
| `monogram` | `DJ` | Text im SVG |
| `title` | `DENVER JOHN` | Untertitel |
| `accent` | `#ff7a00` | Stroke D |
| `accent2` | `#ffb36b` | Stroke J |
| `textColor` | `#f7f3ee` | Monogramm-/Titel-Farbe |
| `animate` | `true` | Stroke-Draw + Shine |

Layout-Beispiel:

```json
{
  "type": "ext:denver-john:logo",
  "kind": "widget",
  "x": 801,
  "y": 24,
  "w": 320,
  "h": 110,
  "props": { "title": "DENVER JOHN", "animate": true }
}
```
