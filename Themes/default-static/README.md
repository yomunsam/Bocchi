# default-static / Bocchi Mono

`default-static` is Bocchi's built-in reference Theme. It is intentionally kept in the same shape as a third-party Theme so Theme authors can copy it, inspect it, and compare their own implementation against the same contract.

## Contract Shape

- `theme.json` declares the Theme identity and uses `runner.kind = "fluid-static"`.
- `config-schema.json` declares Dashboard-editable settings. Presentation text can opt into `textFormat: "inlineColor"` for controlled `[color=#E85D3A]...[/color]` or `[color=accent]...[/color]` spans; site facts belong in `site.yaml` and arrive through `theme-context.json`.
- `templates/` contains Liquid templates rendered by Bocchi's built-in `fluid-static` runner.
- `assets/` contains static files copied into the Theme output. Theme Contract routes stay site-root-relative, while the built-in runner emits relative HTML links so the same output can be served from a domain root or a nested path.

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
