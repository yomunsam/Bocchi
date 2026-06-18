<div align="center">

<h1 align="center">Bocchi</h1>

English / [简体中文](./README.zh.md)

</div>

Bocchi is a personal homepage/blog system shaped by the author's tastes and preferences, with solid compatibility and extensibility.

Key features:

- **Static site**: Deploy to Nginx, GitHub Pages, Cloudflare Pages, and other static hosts—minimal ops overhead and cognitive load.
- **Markdown-first content**: Portable, easy to migrate, and cross-platform friendly.
- **Full admin backend**: Run on a home server or private lightweight host for complete content management and publishing.
- **Modular frontend**: Build frontend themes your way, or use the default theme template.

------

<br>

## Quick Start

**Bocchi Home Server** is meant to run on a home server, NAS, personal VPS, or similar. It includes an admin panel and is **not designed to be exposed directly to the public internet**. Once Home Server is deployed, you can use Bocchi's full feature set.

### Start from binaries

Download stable Bocchi Home Server binaries from [GitHub Releases](https://github.com/yomunsam/Bocchi/releases) and run them. No database, SDK, or runtime dependencies required.

### Start from a container

> TODO Documentation to be added

## Build your own frontend theme

You can fully define your site's look, interactions, and how content is presented—on your own or with help from an AI agent.

> TODO Documentation to be added

------

<br>

## Features overview

> Todo

------

<br>

## Tech stack and third-party dependencies

**Bocchi Home Server**

- .NET 10 (dotNet 10) / ASP.NET Core
- Entity Framework Core + SQLite
- Blazor Server
- ASP.NET Core Identity / OpenID Connect
- Vite + TailwindCSS v4

**Bocchi Cloud Server**

- Hono / TypeScript (placeholder, not yet implemented)

**Bocchi Mono** (default frontend theme)

- Template engine: Liquid
- Web Components
- Native JavaScript / CSS

Bocchi uses the following third-party libraries and toolchain components:

- [Vite](https://vitejs.dev/) (MIT License): frontend asset build tool for Home Server.
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) (MIT License): YAML parser (Markdown frontmatter, etc.).
- [Markdig](https://github.com/xoofx/markdig) (BSD-2-Clause): Markdown parsing.
- [Serilog](https://github.com/serilog/serilog) (Apache-2.0): logging for Home Server.
- [Blazicons.Lucide](https://github.com/kyleherzog/Blazicons.Lucide) (MIT License): icon library for the Home Server admin panel.
- [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) (MIT License): Git operations for content directory management.
- [Fluid](https://github.com/sebastienros/fluid) (MIT License): template rendering for one of the built-in base theme approaches.
- [TailwindCSS](https://tailwindcss.com/) (MIT License): CSS framework for the Home Server admin panel.
- [CodeMirror](https://codemirror.net/) (MIT License): Markdown editor for the Home Server admin panel.

------

<br>

## Development and build

> TODO
