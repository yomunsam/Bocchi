<div align="center">

<h1 align="center">Bocchi</h1>

[English](./README.md) / 简体中文

</div>

Bocchi 是一个依据作者个人喜好、品味而设计的个人主页/Blog系统，并具备良好的兼容性与扩展性。

关键特性：
- 静态站点：部署到Nginx、Github Pages、Cloudflare Pages等静态站点托管服务，极小的运维成本和心智负担。
- 以Markdown文件为主要内容管理方式：便携、易迁移、跨平台兼容性。
- 完整后台：在你的家庭服务器、私人轻量服务器中部署，完整的站点内容管理与发布功能。
- 模块化前端：使用任意你喜欢的方式构建前端Theme，或使用默认前台模板。

------

<br>

## 快速开始

“Bocchi Home Server”被设计为部署在你的家庭服务器、NAS、个人VPS等服务器中，它包含管理后台，设计上**不直接向外提供服务**。部署Bocchi Home Server后，即可使用Bocchi的多种功能。

### 从二进制文件启动

你可从[Github Release](https://github.com/yomunsam/Bocchi/releases)下载稳定版Bocchi Home Server的二进制文件，并运行它。无需数据库、SDK、运行时依赖。

### 从容器启动

> TODO 文档待补充

## 制作自己的前台Theme

你可以自己动手（或借助AI Agent）完全定义站点的前台样式风格、交互与内容展示方式。

> TODO 文档待补充

------

<br>

## 特性概述

> Todo

------

<br>

## 技术栈与第三方依赖

Bocchi Home Server 技术栈：
- .NET 10 (dotNet 10) / ASP.NET Core
- Entity Framework Core + SQLite
- Blazor Server
- ASP.NET Core Identity / OpenID Connect
- Vite + TailwindCSS v4

Bocchi Cloud Server 技术栈：
- Hono / TypeScript （占位， 待实现）

Bocchi Mono (default frontend theme) 技术栈：
- 模板引擎：Liquid
- Web Components
- Native JavaScript / CSS


Bocchi 使用了如下第三方依赖（代码库或工具链）：
- [Vite](https://vitejs.dev/) (MIT License)：Bocchi使用Vite作为Home Server的部分前端资源构建工具。
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) (MIT License)：Bocchi使用YamlDotNet作为YAML的解析器（Markdown frontmatter 等）。
- [Markdig](https://github.com/xoofx/markdig) (BSD-2-Clause)：Bocchi使用Markdig对Markdown文件进行解析。
- [Serilog](https://github.com/serilog/serilog) (Apache-2.0)：Home Server的日志记录工具。
- [Blazicons.Lucide](https://github.com/kyleherzog/Blazicons.Lucide) (MIT License)：Home Server管理面板使用的图标库。
- [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) (MIT License)：内容目录管理等Git操作的实现。
- [Fluid](https://github.com/sebastienros/fluid) (MIT License)：内置的基础Theme处理方式之一的模板渲染引擎。
- [TailwindCSS](https://tailwindcss.com/) (MIT License)：Home Server 管理面板的CSS框架。
- [CodeMirror](https://codemirror.net/) (MIT License)：Home Server 管理面板的Markdown编辑器。

------

<br>

## 开发与构建

> TODO