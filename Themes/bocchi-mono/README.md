# bocchi-mono

`bocchi-mono` is Bocchi's bundled default Theme. It uses the same Theme Contract as third-party Themes and does not define the Fluid Static implementation itself.

## Contract Shape

- `theme.json` declares the Theme identity, uses `runner.kind = "fluid-static"`, and declares `staticAssets` so the Generator copies `assets/` into `/assets`.
- `config-schema.json` declares Dashboard-editable settings. Presentation text can opt into `textFormat: "inlineColor"` for controlled `[color=#E85D3A]...[/color]` or `[color=accent]...[/color]` spans; site facts belong in `site.yaml` and arrive through `theme-context.json`.
- `templates/` contains Liquid templates rendered by Bocchi's built-in `fluid-static` runner.
- `assets/` contains static files copied into the Theme output by the Theme Contract `staticAssets` stage. Theme Contract routes stay site-root-relative, while the built-in runner emits relative HTML links so the same output can be served from a domain root or a nested path.

## Fonts

Only the Latin fonts are bundled: Instrument Serif (400 regular and italic) and Inter Tight (400–700), as latin-subset woff2 files under `assets/fonts/`, each with its SIL OFL 1.1 license (`OFL.txt`). Chinese and Japanese text uses system font stacks (Noto CJK, Source Han, Songti/PingFang, Hiragino, Yu Mincho/Gothic and so on; Windows headings fall back to SimSun), because CJK web fonts would cost visitors several megabytes. No monospace font is bundled: code uses JetBrains Mono if the visitor has it installed, otherwise the system monospace stack.

### Chinese web font

Theme setting `visual.cjkFontStylesheetUrl` (Dashboard: Theme customization → visual) takes an optional CSS stylesheet URL from any font CDN.

- Empty (the default): the site makes no external font requests at all.
- Set: it must be an absolute `https://` URL; anything else is rejected on save and ignored at build time. Pages get a `preconnect` to the stylesheet's origin and a non-blocking stylesheet link (`media="print"` switched to `all` on load, with a `<noscript>` fallback), so a slow font host never blocks rendering. Headings on `zh` / `ja` pages then try `Noto Serif SC` / `TC` / `JP` before the system stack.
- Ask for `display=swap` and only the weights headings use (400 and 500). Providers that split fonts by `unicode-range` only download the slices a page actually needs; a page without Chinese text downloads the stylesheet but no font files.
- Visitors' IP addresses are sent to the font host.

Examples:

- Google Fonts: `https://fonts.googleapis.com/css2?family=Noto+Serif+SC:wght@400;500&display=swap`
- <!-- TODO: more verified CDN examples (e.g. mirrors reachable from mainland China) go here once tested. -->

## Preview Compatibility

Home Server live preview is a first-class build mode. A Theme should:

- Read only `BOCCHI_INPUT_DIR` and write only `BOCCHI_OUTPUT_DIR`.
- Treat `theme-context.json` `build.mode = "live"` as the same page contract as `full`, just backed by temporary input/output directories.
- Keep internal links and static assets tied to Theme Contract routes; the built-in runner rewrites final HTML URLs relative to each output page.
- Leave Admin, Edit, and Preview controls to Home Server. Preview Toolbar is injected into HTML responses.
- Avoid full-screen fixed overlays and very high `z-index` values that would cover the injected toolbar.

## Authoring Notes

The `fluid-static` runner generates Bocchi's standard static route set from the Theme Contract inputs. If a Theme needs a completely different route system or a frontend framework build, use `runner.kind = "process"` instead.

To start a new Theme from this reference implementation, copy the directory outside the Bocchi repo, change `theme.json.id` and `theme.json.name`, then add a Dev Link in `<data>/themes/dev-links.json`. Refreshing Preview runs a new Live Build against the external Theme Root, so template, CSS, manifest, and schema changes do not need to be copied back into DataRoot.

`process` runner Themes should prepare dependencies in their own repo. Bocchi does not run `installCommand` during ordinary Preview or Full Build; missing Node.js, package-manager, or binary dependencies are reported as runner startup/build errors.
