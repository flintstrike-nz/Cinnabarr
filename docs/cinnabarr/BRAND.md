# Cinnabarr brand

| Token | Value | Use |
| --- | --- | --- |
| Cinnabar vermilion | `#E34234` | Primary accent: primary buttons, selection, focus |
| Jellyfin amethyst | `#AA5CC3` | Ecosystem colour: links, info states |
| Gradient bridge | `#D9386A` | Middle of the brand gradient (`135deg`, amethyst → bridge → vermilion) |
| Dark canvas | `#101014` | Dark theme page background, and the header in both themes |
| Card surface | `#1C1C24` | Dark theme cards, modals and menus |

- The header is the dark canvas in both themes, with a 2px gradient border along its bottom edge (`--pageHeaderBorderImage`).
- Tokens live at the top of `frontend/src/Styles/Themes/themes.css` (`--brand*`); the rest of the theme is derived from them.
- Logo: `frontend/src/Content/Images/logo.svg` (transparent mark, used in the app) and `logo-squircle.svg` (on the dark squircle, used for the app icons). The favicon, touch, tile and manifest icons in `Content/Images/Icons` are rendered from the squircle.
