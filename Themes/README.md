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

## Local Theme authoring

The recommended authoring loop is source-run Home Server and link an external Theme repo:

```bash
dotnet run --project Src/HomeServer/Bocchi.HomeServer
```

Create `<data>/themes/dev-links.json` in the active DataRoot:

```json
{
  "schemaVersion": "1.0",
  "links": [
    {
      "id": "my-theme",
      "root": "/Users/yomu/Projects/my-theme",
      "enabled": true,
      "note": "Local theme development"
    }
  ]
}
```

`root` must be an absolute path and must contain `theme.json`. The `id` in the link must match `theme.json.id`. Dev Links are enabled by default only in `Development`; production must opt in with `Bocchi:Themes:AllowDevLinks=true`.

For Docker verification, keep the same contract but use the container path, for example `/theme-dev/my-theme`, and mount the host repo into that path.

## Zip packages

Admin Dashboard installs ordinary Theme zip packages into `<data>/themes/<theme-id>/`. A package can either place `theme.json` at the zip root or under one top-level directory, such as GitHub source downloads.

Package inspection rejects unsafe paths, unsupported contract versions, invalid Theme ids, unsupported runners, and malformed manifests before writing to the installed Theme directory. `process` runner packages require explicit trust confirmation because they execute commands in the Home Server host during build. Upload and inspection never run `installCommand` or any package code.

Theme implementation, Theme configuration, upload cache, build cache, and output all live under DataRoot outside `workspace/`.
