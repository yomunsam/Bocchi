---
name: Bocchi Admin
# 默认「运动服」配色（浅色）。数值以 Client/styles/tokens.css 为准，改配色时两边一起改。
colors:
  bg: '#fbf8f8'
  surface: '#ffffff'
  surface-muted: '#f6f2f3'
  surface-soft: '#efe9eb'
  text: '#241e21'
  text-muted: '#5c5157'
  text-subtle: '#736770'
  border: '#ebe3e6'
  border-strong: '#e0c8d1'
  action: '#b83b70'
  action-strong: '#93264f'
  action-fill: '#b83b70'
  on-action: '#ffffff'
  pink: '#f4a6c0'
  clip-blue: '#4a9fd8'
  clip-yellow: '#f2c94c'
  success-soft: '#e9f8f0'
  success-strong: '#2e7d32'
  warning-soft: '#fff5dc'
  warning-strong: '#7a5710'
  danger-soft: '#fff0f0'
  danger-strong: '#ba1a1a'
typography:
  family: Sora
  headline-lg: { fontSize: 32px, fontWeight: '600', lineHeight: 40px }
  headline-md: { fontSize: 24px, fontWeight: '600', lineHeight: 32px }
  body-md: { fontSize: 14px, fontWeight: '400', lineHeight: 20px }
  label-md: { fontSize: 12px, fontWeight: '500', lineHeight: 16px }
rounded:
  sm: 4px
  md: 6px
  lg: 8px
  xl: 12px
  pill: 999px
spacing:
  unit: 4px
  scale: [4, 8, 12, 16, 24, 32, 48]
---

## Brand & Style
Bocchi Admin is a calm, long-session workspace. The look is minimal with a few small nods to Bocchi: the pink of her hair and tracksuit, and the blue and yellow hair clips (the two marks next to the sidebar logo). Accents stay small; structure comes from neutral surfaces and spacing.

## Palettes
All components use the semantic variables in `Client/styles/tokens.css`. Never hard-code a brand color in a component. Three palettes exist; each overrides only brand and surface variables, for light and dark:

| Palette | How to select | Light | Dark |
| --- | --- | --- | --- |
| Tracksuit (default) | no attribute, or `data-bocchi-palette="tracksuit"` | pink action `#b83b70`, warm off-white surfaces | light pink accent `#f4a6c0`, button fill `#b83b70` |
| Maid | `data-bocchi-palette="maid"` | black and white, action `#2b2528`, pink nav bar | inverted primary: fill `#f2f2f2` with text `#1c1a1b` |
| Clip | `data-bocchi-palette="clip"` | blue action `#286aa3`, yellow nav bar | light blue accent `#8cc4ec`, button fill `#286aa3` |

The attribute is set on `<html>` before first paint from `localStorage["bocchi.dashboard.palette"]`. There is no switcher in the UI yet.

Key variables:
- `--bocchi-action`: links, active icons, small accents. `--bocchi-action-strong` is the darker text variant.
- `--bocchi-action-fill` + `--bocchi-on-action`: background and text of primary buttons and other filled controls. Always pair them; never assume white text.
- `--bocchi-text`, `--bocchi-text-muted`, `--bocchi-text-subtle`: three text levels. `subtle` is for dates, paths, slugs and counts, which people still need to read.
- `--bocchi-success-*`, `--bocchi-warning-*`, `--bocchi-danger-*`: status pills and messages.
- `--bocchi-clip-blue` / `--bocchi-clip-yellow`: decorative only (logo clips, focus ring).

Contrast rules, checked for every palette in light and dark:
- Body text and `text-subtle` reach at least 4.5:1 on `surface`, `bg` and `surface-muted`.
- Status pill text reaches 4.5:1 on its soft background.
- Primary button text reaches 4.5:1 on `action-fill`, and a primary button must stand out from a secondary button next to it.

## Typography
Sora (self-hosted, `Client/styles/fonts.css`) provides a modern, geometric clarity that feels technical yet friendly. To reduce fatigue, the hierarchy is enforced through generous vertical rhythm. 

Headers use a semi-bold weight and tighter letter spacing for a punchy, professional look. Body text is set with comfortable line heights to facilitate scanning of data-heavy tables and reports. Labels utilize a slightly increased letter-spacing to improve clarity at smaller sizes.

## Layout & Spacing
The layout follows a **Fixed Grid** model for the main content area to maintain line-length readability, while the sidebar remains fixed to the viewport.

- **Desktop:** 12-column grid with 24px gutters.
- **Tablet:** 8-column grid with 16px gutters.
- **Mobile:** 4-column grid with 16px margins.
- **Spacing Logic:** Padding and margins come from the `--space-1` to `--space-7` scale (4–48px). Component-internal spacing (like inside a card) should prioritize 16px or 24px padding to maintain the "clean" aesthetic.

## Elevation & Depth
Depth is achieved primarily through **Tonal Layers** and extremely **Ambient Shadows**. 

Surfaces are distinguished by slight shifts in background color (e.g., a white card on the off-white page background). Shadows must be "airy": use a high blur radius (16px to 32px), very low opacity (4-8%), and a hint of the secondary charcoal color in the shadow tint to keep it grounded. Avoid hard shadows or inner glows. Use 1px borders in a very light grey-beige for subtle definition on interactive elements.

## Shapes
The shape language is consistently **Rounded**, reinforcing the approachable personality. 

Radii come from `--radius-sm/md/lg/xl` (4/6/8/12px). Buttons and inputs use 8px; cards and panels use 8–12px. Selection indicators (like active menu items) may use a fully rounded/pill shape for clear visual distinction.

## Components
- **Buttons:** Primary buttons declare `bocchi-button--primary` (or use their own class with `action-fill` / `on-action`). Disabled primary buttons switch to `surface-muted` with a border instead of fading with opacity. Secondary buttons use `surface` with a 1px border.
- **Input Fields:** `surface` background with a 1px border; focus shows `--bocchi-focus-ring`. Labels stay visible above the field.
- **Cards:** White (`surface`) with an 8–12px radius, a 1px border and a very soft shadow.
- **Lists:** Horizontal dividers, soft hover state, no zebra rows. On phones, titles wrap to two lines instead of being cut to a few characters.
- **Status pills:** Soft background with the matching strong text (`success`, `warning`, `danger`, `info`, `neutral`).
- **Iconography:** Lucide line icons. Active states use `action`, inactive ones `text-muted`.
- **Help text:** No descriptive paragraphs under page titles. A rule the user must know (who can sign in, what a button really does) goes right next to the control as one `.bocchi-hint` line. Optional explanations use `BocchiInfoTip` (a focusable icon with a `.bocchi-tooltip`), which works on hover, keyboard focus and tap. Do not rely on the native `title` attribute for help.
- **Touch targets:** At ≤620px, interactive controls are at least 44px (`base.css`). The Markdown toolbar opts out of that rule and lays itself out as a wrapping 44px grid, so no button is pushed off screen.

## Dark Mode
Dark mode keeps the same structure with a faint warm undertone in the tracksuit palette (maid is neutral grey, clip is cool blue-grey). Surfaces step up from `bg` to `surface` to `surface-muted` to `surface-soft`. In tracksuit and clip, the light accent (`action`) is used for icons and borders while buttons keep a deeper `action-fill`; maid inverts its primary button to light grey. Shadows are pure black at 28–40% opacity.
