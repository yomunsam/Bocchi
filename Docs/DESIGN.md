---
name: Milk & Berry Admin
colors:
  surface: '#fbf9f7'
  surface-dim: '#dbdad8'
  surface-bright: '#fbf9f7'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f5f3f1'
  surface-container: '#efedeb'
  surface-container-high: '#eae8e6'
  surface-container-highest: '#e4e2e0'
  on-surface: '#1b1c1b'
  on-surface-variant: '#574146'
  inverse-surface: '#30302f'
  inverse-on-surface: '#f2f0ee'
  outline: '#8a7176'
  outline-variant: '#ddbfc5'
  surface-tint: '#ab2c5d'
  primary: '#ab2c5d'
  on-primary: '#ffffff'
  primary-container: '#f06292'
  on-primary-container: '#5e002b'
  inverse-primary: '#ffb1c5'
  secondary: '#4A454E'
  on-secondary: '#ffffff'
  secondary-container: '#e5dde8'
  on-secondary-container: '#66606a'
  tertiary: '#8C8590'
  on-tertiary: '#ffffff'
  tertiary-container: '#949392'
  on-tertiary-container: '#2c2c2b'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#ffd9e1'
  primary-fixed-dim: '#ffb1c5'
  on-primary-fixed: '#3f001b'
  on-primary-fixed-variant: '#8b0e45'
  secondary-fixed: '#e8e0eb'
  secondary-fixed-dim: '#ccc4cf'
  on-secondary-fixed: '#1e1a22'
  on-secondary-fixed-variant: '#4a454e'
  tertiary-fixed: '#e4e2e0'
  tertiary-fixed-dim: '#c7c6c4'
  on-tertiary-fixed: '#1b1c1b'
  on-tertiary-fixed-variant: '#464746'
  background: '#fbf9f7'
  on-background: '#1b1c1b'
  surface-variant: '#e4e2e0'
typography:
  headline-lg:
    fontFamily: Sora
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 40px
    letterSpacing: -0.02em
  headline-md:
    fontFamily: Sora
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.01em
  headline-sm:
    fontFamily: Sora
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  body-lg:
    fontFamily: Sora
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Sora
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  label-lg:
    fontFamily: Sora
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 20px
    letterSpacing: 0.01em
  label-md:
    fontFamily: Sora
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
  headline-lg-mobile:
    fontFamily: Sora
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  unit: 8px
  gutter: 24px
  margin-desktop: 32px
  margin-mobile: 16px
  container-max-width: 1440px
---

## Brand & Style
The brand personality is professional and organized, yet retains a distinctive approachable warmth. It targets power users who spend long hours in an administrative environment, prioritizing the reduction of visual fatigue through a "Milk & Berry" aesthetic. 

The design style is **Corporate / Modern** with a **Minimalist** foundation, punctuated by subtle "cute" accents. It utilizes heavy whitespace and a refined color balance to create an airy, focused atmosphere. The emotional response is one of calm efficiency, moving away from overstimulation toward a sophisticated, high-utility workspace.

## Colors
This design system utilizes a sophisticated palette to ensure long-term legibility and comfort. 

- **Primary (Berry):** A refined pink used exclusively for primary action buttons, active states, and critical status indicators. It serves as a high-contrast focal point rather than a structural element.
- **Neutral Surface (Milk):** The primary background uses a warm off-white (`#FDFBF9`) to eliminate the harsh glare of pure white.
- **Secondary (Charcoal):** A deep, warm charcoal used for body text and headers to ensure high readability without the "vibration" of pure black on white.
- **Surface Container:** A subtle warm grey (`#F5F3F1`) is used for sidebars and section nesting to create depth without the need for heavy lines.

## Typography
Sora provides a modern, geometric clarity that feels technical yet friendly. To reduce fatigue, the hierarchy is enforced through generous vertical rhythm. 

Headers use a semi-bold weight and tighter letter spacing for a punchy, professional look. Body text is set with comfortable line heights to facilitate scanning of data-heavy tables and reports. Labels utilize a slightly increased letter-spacing to improve clarity at smaller sizes.

## Layout & Spacing
The layout follows a **Fixed Grid** model for the main content area to maintain line-length readability, while the sidebar remains fixed to the viewport.

- **Desktop:** 12-column grid with 24px gutters. Sections are separated by large 48px or 64px vertical gaps to emphasize an "airy" feel.
- **Tablet:** 8-column grid with 16px gutters.
- **Mobile:** 4-column grid with 16px margins.
- **Spacing Logic:** All padding and margins are increments of an 8px base unit. Component-internal spacing (like inside a card) should prioritize 16px or 24px padding to maintain the "clean" aesthetic.

## Elevation & Depth
Depth is achieved primarily through **Tonal Layers** and extremely **Ambient Shadows**. 

Surfaces are distinguished by slight shifts in background color (e.g., a card using pure white sitting on a Milk-colored background). Shadows must be "airy": use a high blur radius (16px to 32px), very low opacity (4-8%), and a hint of the secondary charcoal color in the shadow tint to keep it grounded. Avoid hard shadows or inner glows. Use 1px borders in a very light grey-beige for subtle definition on interactive elements.

## Shapes
The shape language is consistently **Rounded**, reinforcing the approachable personality. 

Standard components like buttons and input fields use a 12px (`0.5rem`) radius. Larger containers, such as dashboard cards and modals, use a 16px (`1rem`) radius. Selection indicators (like active menu items) may use a fully rounded/pill shape for clear visual distinction.

## Components
- **Buttons:** Primary buttons are solid "Berry" with white text. Secondary buttons use a ghost style with 1px charcoal borders.
- **Input Fields:** Use the "Milk" background with a 1px border that shifts to "Berry" only on focus. Labels should always be visible above the field in "Charcoal."
- **Cards:** White backgrounds with 16px rounded corners and a very soft ambient shadow. No borders.
- **Lists & Tables:** Use subtle horizontal dividers in a light neutral tint. Avoid alternating row colors; instead, use a soft hover state change.
- **Chips:** Small, pill-shaped elements with light "Berry" backgrounds (10% opacity) and "Berry" text for a soft, professional accent.
- **Iconography:** Use line icons with a consistent 2px stroke weight, utilizing the "Berry" color for active states and "Charcoal" for inactive.