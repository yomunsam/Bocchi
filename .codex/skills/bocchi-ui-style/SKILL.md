---
name: bocchi-ui-style
description: Bocchi M4 Dashboard UI style guardrails for designing, generating, prototyping, or reviewing Home Server admin screens, UI mockups, Blazor pages, HTML prototypes, screenshots, and UI-Design.md updates. Use when work touches Bocchi Admin visual direction, Dashboard shell, content lists, editor, setup/login, settings, preview toolbar, responsive layout, theme tokens, or image-generation prompts.
---

# Bocchi UI Style

## Overview

Use this skill to keep Bocchi M4 UI work aligned with the confirmed direction: a soft, app-like personal publishing tool for ordinary creators, not a dense professional operations console.

The target feeling is gentle, young, personal, and clear: anime-inspired in mood, but never a copy of any character, scene, costume, logo, band, or copyrighted visual identity.

## Core Direction

Treat Bocchi Admin as a personal Blog/CMS app for people aged roughly 18-35. It should be approachable for both PC-native users and mobile-native users.

Prefer:

- A soft personal publishing app, not an enterprise dashboard.
- Low to medium information density, with clear breathing room.
- App-like content lists instead of wide professional tables where possible.
- Friendly plain-language status such as `Ready`, `Draft`, `Needs a quick look`, `Preview`, and `Publish looks okay`.
- Mobile-responsive shapes from the start: single-column rows, touch-friendly actions, collapsible navigation, and menu-based secondary actions.
- Muted powder blue, low-saturation baby pink, pale lavender, warm off-white, soft gray, deep ink text, small mint success, and gentle amber warning.

Avoid:

- GitHub-like repository UI, hard-core logs, terminal blocks, dense ops consoles, and analytics-heavy dashboards.
- Saturated hot pink, neon accents, one-note blue/purple themes, beige-only themes, dark-slate dominance, or glassmorphism.
- Many small bottom widgets, nested cards, wide dense tables, or controls that cannot collapse for mobile.
- Any direct anime character copy: no recognizable face, hair, outfit, guitar pose, band logo, scene, or character name.

## Fixed UI Rules

- Top-left logo is text only: `Bocchi`. Do not add an icon, mascot, mark, or symbol beside it unless the user later asks.
- Dashboard dark-mode / appearance switching is a compact dropdown such as `外观: Auto` or `Auto`, not a wide `Light / Dark / Auto` segmented control. Do not call this control `Theme`; reserve `Theme` for the frontend business Theme Contract.
- Main content should usually be a simple list/feed of content items with title, type chip, date/path, gentle status, and one obvious action.
- Use raw logs only inside a deliberate advanced Build detail screen. On normal overview screens, translate build/log concepts into human status and next actions.
- Keep panel count low. A main list plus one light helper/status panel is usually enough for the home screen.
- Design desktop screens so the same information can become a mobile single-column list without redesigning the whole IA.

## Workflow

1. Read the relevant M4 doc section before making UI decisions, especially `Docs/Milestones/M4/M4.md` and `Docs/Milestones/M4/UI-Design.md` if it exists.
2. State the screen being designed or reviewed: Overview, Content List, Editor, Setup, Login, Settings, Build, Preview Toolbar, or Mobile layout.
3. Reduce first. Remove nonessential widgets, columns, logs, charts, and status panels before adding visual polish.
4. Pick the responsive model before finalizing desktop layout. Prefer list rows, stacked metadata, compact dropdowns, and action menus.
5. Apply the Bocchi palette softly. If the pink becomes attention-grabbing, lower saturation before changing structure.
6. When generating visuals, use image generation for concept screenshots before implementation if the user is still discussing style.
7. When implementing, preserve the same rules in CSS/components and verify at desktop plus phone-sized viewport.
8. When updating docs, record the accepted style rules and any rejected directions so future UI work does not drift back.

## Image Generation Prompt Skeleton

Use this as a base and adapt only the screen-specific content:

```text
Use case: ui-mockup
Asset type: high-fidelity Bocchi Admin UI concept screenshot, 1440x900
Primary request: Create a soft, app-like personal publishing dashboard for Bocchi Admin. It should feel friendly and clear for ordinary Blog creators, not like an enterprise CMS, GitHub repo dashboard, or developer console.
Copyright/style boundary: Bocchi has anime-inspired mood only. Do not copy or depict any recognizable copyrighted character, face, hairstyle, outfit, instrument pose, band name, logo, or scene.
Audience: 18-35 personal blog creators, including mobile-native and PC-native users.
Layout: text-only Bocchi logo, compact collapsible sidebar, soft top bar, compact Theme dropdown, simple main content list/feed, and at most one light helper/status panel.
Main content: app-like content rows with title, type chip, path/date, gentle status, and one obvious action such as Preview or Continue writing.
Tone: shy-but-bright personal creativity, bedroom writing/music energy, gentle youth culture, mature cute rather than childish.
Palette: powder blue, low-saturation baby pink, pale lavender, warm off-white, soft gray, deep ink text, small mint success, gentle amber warning. Pink must be muted, not hot or neon.
Avoid: raw logs, terminal blocks, dense tables, analytics dashboard, many small widgets, saturated pink, glassmorphism, mascot, direct anime character art, and any copied anime details.
```

## Review Checklist

- Does this still feel like a normal-person publishing app rather than a professional workstation?
- Can the main content collapse to mobile without turning into a cramped table?
- Is the screen trying to show too many boxes, counters, logs, or diagnostics?
- Is the pink soft enough to support the UI without becoming the loudest thing on the page?
- Is every technical state translated into a friendly status or action unless the user explicitly opened advanced details?
- Is `Bocchi` still text-only in the logo area?
- Is Dashboard appearance / dark-mode switching compact, and clearly separate from frontend Theme selection?
- Are anime influences limited to abstract mood and palette, with no recognizable copied character details?

## Screen Notes

For Overview, prefer `Continue writing`, recent drafts, preview shortcuts, and publish readiness. Do not show raw build logs here.

For Content List, prefer an app-like feed/list. Use type chips and status chips, not many table columns.

For Editor, keep Markdown as the core but avoid making the first screen look like an IDE. Metadata can become a side drawer or mobile tab.

For Build/Publish, expose friendly publish steps first. Put raw logs behind an advanced detail view.

For Preview Toolbar, keep it light, collapsible, and non-blocking. It should say Preview, return to Admin, and offer Edit only when route mapping is clear.
