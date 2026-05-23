# Themes/

This directory contains repository-shipped Theme source trees.

`default-static/` is the canonical source for Bocchi's built-in reference Theme. It is embedded into the `Bocchi.Theme.DefaultStatic` assembly during publish and materialized into `<data>/themes/default-static/` at runtime. The materialized DataRoot copy is the editable running instance; this directory remains the source-of-truth example for maintainers and third-party Theme authors.

Third-party Themes can copy this shape:

- `theme.json`
- `config-schema.json`
- `templates/`
- `assets/`
- `README.md`

Use `runner.kind = "fluid-static"` for Themes that follow Bocchi's built-in static route model and Liquid templates. Use `runner.kind = "process"` for framework or custom-build Themes.
