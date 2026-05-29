# Bocchi Architecture

本文档描述 Bocchi 的目标架构、模块边界、数据契约和近期实现原则。它以 `README.md` 中的初步构想为准，将项目收束为一个“内网内容工作台 + 静态站点生成器 + 可选云端运行时”的个人主页/Blog 系统。

## 1. 产品定位

Bocchi 是个人自用的主页与 Blog 系统。它优先服务以下目标：

- 个人内容长期可控，Markdown 文件和媒体文件应当能脱离系统直接保存与迁移。
- 对外页面优先静态化，适合发布到本地目录、Cloudflare Pages、GitHub Pages、Vercel 等托管环境。
- 后台管理只运行在 NAS、个人服务器或本机内网环境，不作为公网业务 API 暴露。
- 前端 Theme 技术栈应保持开放，Home Server 只约束输入/输出契约，不强行绑定某个前端框架。

非目标：

- MVP 阶段不做多租户或复杂多角色协作 CMS；M4 起具备 ASP.NET Core Identity 多用户基础，但业务权限只保留 `Admin` 一个角色。
- MVP 阶段不把 Home Server 设计成公网 API Server。
- MVP 阶段不要求所有内容都进数据库，文件仍然是内容事实来源。
- Cloud Server 只做预留，不在没有明确动态需求前提前实现复杂后端。

## 2. 系统组成

### 2.1 Home Server

Home Server 是 Bocchi 的内容管理系统、Dashboard 和静态站点构建器。

职责：

- 管理文章、页面、作品、短文、友链、站点设置和预留照片墙内容。
- 扫描并解析 workspace 中的 Markdown、frontmatter 和媒体文件。
- 维护 SQLite 应用状态，例如 ASP.NET Core Identity、Setup 状态、构建缓存、发布历史、后台界面状态、主题配置和内容索引。
- 生成标准化内容数据，传递给 Theme。
- 调用 Theme 构建流程，产出可部署的静态站点目录。
- 生成 RSS、Sitemap、搜索索引等站点级产物。
- 管理发布目标，例如本地输出目录和 Cloudflare Pages 相关流程。

技术方向：

- 基于 .NET 10 / ASP.NET Core。
- 后台 UI 采用 Blazor Web App + InteractiveServer，Dashboard 基础 URL 为 `/Admin`。
- 用户系统采用 ASP.NET Core Identity；底层多用户但非多租户，业务权限只保留 `Admin` 角色。
- 数据访问采用 EF Core + SQLite。SQLite 不替代 Markdown 文件作为内容源，EF Core 只保存 Identity、设置、索引、状态和构建记录等 Home Server 应用数据。
- Markdown 解析建议优先使用成熟 .NET Markdown 处理库。

### 2.2 Page Frontend

Page Frontend 是对外访问的个人主页与 Blog 页面。它由 Theme 提供实现。

职责：

- 根据 Home Server 生成的标准化数据渲染首页、文章、页面、作品、短文、友链等页面。
- 输出静态资源和页面文件。
- 支持 RSS、Sitemap 和搜索索引的消费或展示。
- 在将来需要评论、动态短文、统计等功能时，能够接入 Cloud Server。

默认方向：

- 默认 Theme 先使用随 Home Server 分发的 Fluid/Liquid 风格模板 renderer，以最小依赖完成真实可用、可复制修改的静态站点输出。
- 允许其他 Theme 使用 SvelteKit、Astro、Next static export、Blazor / Razor static renderer、纯 HTML 模板、Fluid/Liquid 类模板或任意可执行构建流程。
- Home Server 不直接干涉 Theme 的内部技术栈，只读取描述文件、写入输入数据、调用 runner、收集输出并校验 manifest。
- SvelteKit 作为后续动态化前台的一等可选路线保留；短文 / 推文动态化、Cloud JSON 拉取、Mastodon 生态对接等不要求 M5 默认 Theme 提前承担 Node.js 依赖。

Home Server 预览模式：

- Home Server 内部的前台站点基础 URL 为 `/`，但它不是公网动态站点，而是登录后的实时预览入口。
- Dashboard 基础 URL 为 `/Admin`，通过按钮在新标签页打开 `/`。
- `/` 下的预览页面需要登录；直接打开前台站点也必然处于 Preview 模式。
- 预览 HTML 可以由 Home Server 的 Preview Host 注入浮动工具栏，用于提示 Preview 状态、返回 Dashboard，并在文章 / 页面 / 作品详情页跳转到后台编辑。
- 预览静态资源不应裸放在公开 web root 中，应通过受授权的 Preview Host 或文件端点返回。

### 2.3 Cloud Server

Cloud Server 是可选组件，用于承载必须在线运行的少量功能。

可能职责：

- 评论系统。
- 动态短文发布或同步。
- Webmention、订阅回调、轻量统计。
- 需要权限控制或边缘函数的动态 API。

MVP 阶段只保留接口边界和目录预留，不实现具体服务。

## 3. DataRoot 与 Workspace

Bocchi 在物理上做严格切分：**DataRoot** 是 Home Server / Bocchi 的持久化数据根，**workspace** 只指用户创作内容目录。这个命名边界是 M2 目录模型的核心约束。

### 3.1 切分原则

- **workspace**：用户的纯创作资产 —— Blog、独立页面、作品集、短文、友链、站点设置。它必须满足：
  - 可独立打包、迁移、备份。
  - 可作为独立 Git 仓库（包括未来连接 GitHub 等）。
  - 仅包含原始 Markdown 与原始媒体；**禁止出现任何构建产物 / 派生媒体**（如 webp、缩略图、HTML、搜索索引）。
  - 不包含任何 Theme 专有配置或 Theme 实现 —— 这些都属于 Bocchi 这个程序的实现细节，不属于内容。
  - 设计宗旨：Bocchi 这个程序未来可能被遗弃、重构、替换；workspace 应当能"一键带走"，不背负任何 Bocchi 痕迹。
- **DataRoot 运行数据**：Home Server / Bocchi 的实现状态 —— SQLite 状态、Data Protection keys、Theme 实例配置、Theme 实现副本、构建缓存、衍生媒体、构建产物、日志。它们位于 workspace 之外，可按职责备份、清理或重建。

### 3.2 默认目录布局

```text
data/                                 <-- DataRoot（发布后程序 ./data；Docker /app/data）
  workspace/                          <-- 用户内容 workspace（可独立 / 可作为 Git 仓库）
    README.md                         <-- 自动生成，说明本目录的"源工程"性质
    .gitignore                        <-- 自动生成
    posts/
      2026/                           <-- 年份一级分类（强约束）
        hello-bocchi/                 <-- 文件夹名即 slug
          index.md                    <-- frontmatter (YAML) + Markdown 正文
          assets/                     <-- 仅放该篇引用的原始媒体
            cover.jpg
    pages/
      about/                          <-- 独立页面无年份层
        index.md
        assets/
    works/
      2024/
        my-game/
          index.md
          assets/
    notes/
      2026/                           <-- 短文按年份 / 日期分目录
        0513/
          2030-k7p9xq2m/              <-- HHmm + 8 位短 id
            index.md
            assets/
    friends/
      friends.yaml                    <-- 友链：单文件 YAML 列表
    photos/                           <-- M2 仅占位
    site/
      site.yaml
      navigation.yaml
  state/                              <-- Home Server / Bocchi 状态
    bocchi.sqlite
    data-protection-keys/
    theme-config/                     <-- Theme 实例配置（与具体 Theme 绑定，不可移植）
  themes/                             <-- 可见 Theme 实例
    default-static/
    my-theme/
    dev-links.json                    <-- 开发期外部 Theme Root 映射
    .backups/                         <-- Theme zip 更新前备份
  cache/
    derivatives/                      <-- 衍生媒体（webp / 缩略图）
    theme-input/                      <-- Theme 输入数据
    theme-upload/                     <-- Theme zip 上传检查与 staging
  output/                             <-- 构建产物
    public/
      .bocchi-manifest.json
  logs/
```

开发期不默认使用项目下的 `data/`，而是使用 Home Server 项目下的 `.bocchi-dev-data/`。原因是源码中已有 `Data/` 目录，macOS / Windows 默认大小写不敏感文件系统会把 `data/` 与 `Data/` 合并，导致运行数据混入源码树。

强约束（M2 起生效）：

- `posts/`、`works/`、`notes/`、`photos/` 强制使用 **年份目录** 作为一级分类，年份正则 `^\d{4}$`。
- Post / Work 单篇为目录形式：`<kind>/<year>/<slug>/index.md` + `assets/`。`assets/` 仅放该篇原始媒体，frontmatter 中以相对路径引用（如 `cover: assets/cover.jpg`）。
- Pages 不按年份分类（独立页面是"地点"而不是"事件"）。
- 短文为目录型内容：`notes/<year>/<MMdd>/<HHmm>-<id>/index.md` + `assets/`；`id` 为 8 位小写字母数字，写入 frontmatter，并作为公开 URL `/notes/{id}/` 的稳定标识。
- frontmatter 一律 YAML（`---` 边界）。
- workspace 内禁止出现派生扩展名（`*.webp` 等）；扫描器会发出 warning。
- `state/`、`themes/`、`cache/`、`output/`、`logs/` **位于 workspace 根之外**，不会被 workspace 的 Git 仓库纳入索引。

### 3.3 Workspace 作为 Git 仓库

M2 起，Bocchi 通过 `LibGit2Sharp` 把 workspace 识别为 Git 工作区，并在内网后台提供：

- `IsRepository` / `Init` / `Status` / `Commit`（本地）。

远程（push / pull、GitHub 接入、凭据存储）作为发布管线的一部分，留给 M7。这一推迟是有意的：在没有发布管线之前提前引入凭据/Webhook 只会带来不必要的复杂度。详见 `Docs/Milestones/M2/M2.md` §3.6。

## 4. 内容模型

MVP 内容模型分为以下块面。

### 4.1 Post

文章。基于 Markdown 文件，支持标题、slug、发布时间、更新时间、分类、标签、草稿、摘要、封面图等字段。

```yaml
---
type: post
title: "文章标题"
slug: "hello-bocchi"
status: draft
publishedAt: 2026-05-13T20:00:00+08:00
updatedAt: 2026-05-13T20:00:00+08:00
category: "随笔"
tags:
  - blog
  - personal
summary: "摘要文本"
cover: "/media/images/cover.jpg"
---
```

### 4.2 Page

独立页面，例如 About。基于 Markdown 文件，通常不进入文章时间线。

字段建议：

- `title`
- `slug`
- `status`
- `order`
- `showInNavigation`
- `summary`
- `template`：可选 Page template name，默认 `normal`。这里只保存模板名称，不保存 Theme id 或关联记录；如果当前 Theme 不再声明该模板，Dashboard 仍保留原 name 并提示不可用。

### 4.3 Work

作品。需要列表页和详情页。

字段建议：

- `title`
- `slug`
- `status`
- `role`
- `period`
- `cover`
- `links`
- `stack`
- `summary`
- `featured`

### 4.4 Note

短文。类似 Twitter 的轻量内容流，支持图片、视频等媒体引用。

字段：

- `id`（8 位小写字母数字，必填，公开 URL 为 `/notes/{id}/`）
- `status`
- `publishedAt`
- `text`
- `media`
- `tags`

短文采用 **目录型 Markdown 内容**，按 `notes/<year>/<MMdd>/<HHmm>-<id>/index.md` 组织。目录中的时间用于 workspace 浏览和排序辅助，不参与公开 URL；frontmatter `id` 是永久公开标识。短文正文即 Markdown 正文，不在 frontmatter 中重复 `text` 字段。短文媒体位于同目录 `assets/`，生成时输出到 `/media/notes/{id}/{file}`。

### 4.5 Friend Link

友链。可以使用 Markdown、YAML 或 JSON 管理。

字段建议：

- `name`
- `url`
- `avatar`
- `description`
- `tags`
- `status`
- `order`

### 4.6 Site Settings

站点设置。建议放在 `site/site.json`。

字段建议：

- 站点标题、描述、语言、时区、基础 URL。
- 作者信息。
- 社交媒体链接。
- 导航。
- 默认 Theme。
- RSS、Sitemap、搜索等开关。

### 4.7 Frontend Menu

前台 Menu v1 是单个站点级 `primary menu`，由 Dashboard 的 `/Admin/Site/Navigation` 读写 `workspace/site/navigation.yaml`。Theme 自行决定桌面端、移动端和嵌套层级的展示方式；Home Server 只保存语义树和 target。

`navigation.yaml` 使用正式 Menu tree，不再兼容旧的 `title/href` 扁平结构：

```yaml
items:
  - id: home
    label: i18n://common@menu.home
    target:
      type: builtin
      value: home
    children:
      - id: about
        label: About
        target:
          type: page
          value: about
        children: []
```

Menu item 字段：

- `id`：稳定节点 id，由 Dashboard 生成并保留。
- `label`：可选展示文本；可以是普通文本，也可以是 `i18n://common@key` / `i18n://theme@key`。
- `target`：可选语义目标，不直接保存 URL。省略时表示待配置项；如果有可输出子项，Theme input 会把它作为非链接分组节点。
- `children`：嵌套子项，最大深度与 Category tree 一致，为 5 层。

Target 类型固定为：

- `builtin`：内置页面，首批 `home`、`posts`、`works`、`notes`、`friends`。
- `themePage`：当前 Theme manifest 声明的特殊页面。
- `page`：自定义 Page，值为 Page slug。
- `postCategory`：Post Category，值为稳定 slug。

Generator 会把可解析 target 转成 `navigation.json` 中的 `href`。无 target 的叶子项不会进入公开 Theme input；无 target 且有可输出子项时会作为 `href: null` 的分组节点输出。无法解析的 target 在 Dashboard 中保留并提示，但不会进入公开 Theme input，避免生成死链接。

### 4.8 Category Slug

Post Category tree 节点拥有稳定 `slug`。Dashboard 新建节点时可由 name 自动生成 slug，保存时保证同一棵 Post category tree 内 slug 非空且唯一。已有 slug 不随 rename 自动改变。

Post Category URL 固定为：

```text
/posts/categories/{slug}/
```

### 4.9 Photo

照片墙预留。M2 仅在 workspace 约定 `photos/<year>/...` 占位目录，不实现解析。完整实现留给后续专门的"照片墙"里程碑。

字段建议：

- `title`
- `takenAt`
- `location`
- `media`
- `albums`
- `tags`
- `description`

## 5. SQLite 职责

SQLite 用于 Home Server 的应用状态、管理状态和派生索引，不作为内容唯一事实源。M4 起，Home Server 的 SQLite 访问应优先通过 EF Core 和 migration 管理；早期为扫描 / 构建打通闭环的手写 `Microsoft.Data.Sqlite` store 不应继续扩大边界。

适合进入 SQLite 的内容：

- ASP.NET Core Identity 用户、角色、登录、外部登录绑定和禁用状态。
- Setup 完成状态、第一个 Admin、数据库 schema / migration 状态。
- GitHub 与 OpenID Connect Provider 的启用状态、client id、受保护的 client secret、claim mapping 等配置。
- 内容文件路径、内容 hash、解析状态、错误信息。
- 构建任务、构建日志、构建产物 manifest。
- 发布目标、发布历史和最近一次成功发布记录。
- Theme 配置值和配置 schema 缓存。
- 后台 UI 偏好，例如最近打开的栏目、草稿筛选状态。
- 媒体引用索引，用于发现孤儿资源或失效引用。

不建议只存在于 SQLite 的内容：

- 文章正文。
- 页面正文。
- 作品详情正文。
- 用户希望长期迁移和独立备份的媒体文件。

## 6. 构建流水线

Home Server 的构建流程建议分为固定阶段。

1. 扫描 workspace。
2. 解析 Markdown、frontmatter 和站点配置。
3. 校验内容模型、slug、链接、媒体引用和发布时间。
4. 生成规范化内容图。
5. 写入 Theme 输入数据。
6. 合并 Theme 配置。
7. 执行 Theme 构建命令。
8. 复制媒体和静态资源。
9. 生成 RSS、Sitemap、搜索索引和 manifest。
10. 校验输出目录。
11. 发布到目标位置。

构建应支持两种模式：

- Full Build：清理输出目录并完整生成。
- Incremental Build：基于内容 hash、依赖图和 Theme manifest 做增量更新。

MVP 可以先实现 Full Build，再逐步扩展增量构建。

## 7. Theme Contract v1

Theme Contract 是 Home Server 和 Page Frontend 之间的核心边界。

### Theme 来源与 Resolver

Theme manifest 的低层读取只负责从一个 Theme Root 读取 `theme.json`。Theme 来源解析由统一的 Theme Resolver / Catalog 负责，避免 Generator、Home Server 和 Dashboard 各自拼接路径。

当前来源类型：

- `builtIn`：随 Bocchi 分发并在需要时物化到 `<data>/themes/default-static/` 的默认参考 Theme。
- `installed`：位于 `<data>/themes/<theme-id>/` 的已安装 Theme。
- `devLink`：开发期通过 `<data>/themes/dev-links.json` 指向外部绝对路径的 Theme Root。
- `packageCandidate`：zip 上传 inspection 阶段的候选 Theme，只有安装成功后才成为 `installed`。

解析优先级：

1. Development 环境默认启用 Dev Link；Production 默认忽略 `dev-links.json`，除非显式设置 `Bocchi:Themes:AllowDevLinks=true`。
2. 启用的 Dev Link 可覆盖同 id 的 installed Theme，并在 Catalog 中标记 shadow 状态。
3. Installed Theme 从 `<data>/themes/<theme-id>/` 解析。
4. `default-static` 在需要时由内置资源补齐到 DataRoot。

`dev-links.json` 是开发状态，不进入 `workspace/`，也不改变普通 Theme 包结构。示例：

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

Dev Link 的 `root` 必须是绝对路径，且 `theme.json.id` 必须与 link id 一致。Docker 验证时使用容器内挂载路径，例如 `/theme-dev/my-theme`。

### 7.1 Theme 目录结构

```text
Theme/
  theme.json
  config-schema.json
  package.json
  src/
  static/
```

非 Node.js Theme 可以没有 `package.json`，但必须在 `theme.json` 中声明 runner。M3 早期的 `build.command` 可视为 `runner.kind = process` 的兼容形态。

### 7.2 theme.json

`theme.json` 描述 Theme 元信息和构建方式。

```json
{
  "id": "default-static",
  "name": "Bocchi Mono",
  "version": "0.1.0",
  "contractVersion": "1.0",
  "inputDir": "../../cache/theme-input",
  "outputDir": "build",
  "runner": {
    "kind": "fluid-static",
    "entry": "fluid"
  },
  "staticAssets": [
    {
      "from": "assets",
      "to": "/assets",
      "include": ["**/*"],
      "exclude": ["**/*.map", "**/.DS_Store"]
    }
  ],
  "features": {
    "posts": true,
    "pages": true,
    "works": true,
    "notes": true,
    "friends": true,
    "photos": false,
    "search": false
  },
  "pageTemplates": [
    {
      "name": "normal",
      "displayName": "i18n://theme@theme.defaultStatic.pageTemplate.normal"
    },
    {
      "name": "about",
      "displayName": "i18n://theme@theme.defaultStatic.pageTemplate.about"
    }
  ],
  "specialPages": [
    {
      "name": "calculator",
      "displayName": "Calculator",
      "route": "/calculator/"
    }
  ]
}
```

`runner.kind = process` 用于第三方 Theme：

```json
{
  "id": "my-svelte-theme",
  "name": "My Svelte Theme",
  "version": "0.1.0",
  "contractVersion": "1.0",
  "inputDir": "../../cache/theme-input",
  "outputDir": "build",
  "runner": {
    "kind": "process",
    "command": "pnpm build",
    "installCommand": "pnpm install --frozen-lockfile"
  }
}
```

M3 代码中的 `build.command` 是 process runner 的早期形态。M5 起文档以 `runner` 为准，代码实现可以保留 `build.command` 作为兼容别名。

Theme 可以声明两类 Page 相关能力：

- `pageTemplates[]`：可编辑 Page 的渲染模板声明，字段为 `name` 和 `displayName`。`normal` 是强制存在的模板；Theme 未声明时，Home Server 合成 `normal` fallback。Page frontmatter 只保存 template name，不保存 Theme id 或关联记录。
- `specialPages[]`：Theme 自己提供的特殊页面声明，字段为 `name`、`displayName` 和 `route`。`route` 必须是站点根相对路径，例如 `/calculator/`；Home Server 不为特殊页面生成内容，只允许 Menu 指向它。

`displayName` 可以是普通文本，也可以是显示值引用：`i18n://common@key` 或 `i18n://theme@key`。`common@` 表示 Bocchi 通用前台文案作用域，`theme@` 表示当前 Theme manifest 提供的私有文案作用域。本轮只把该约定用于 Dashboard 展示和 Theme manifest 声明，不做全字段全局改造。

Theme 可以通过 `staticAssets[]` 声明要原样复制到静态站点输出的资源目录。`from` 是相对 Theme Root 的源目录，`to` 是以 `/` 开头的站点根相对目标目录；`include` / `exclude` 使用相对 `from` 的 glob，缺省 `include` 为 `["**/*"]`。该能力只做复制：不做 minify、hash 文件名、bundling、CSS/JS 路径重写，也不执行 Theme 代码。复制发生在 runner 结束之后、Theme 输出收集之前；如果目标文件已存在，构建失败而不是覆盖。`from` 不能是绝对路径、不能包含 `..`、不能指向 `outputDir`，也不能复制 `.git/` 或 `node_modules/`；symlink / reparse point 指向 Theme Root 外部时会被拒绝。

### 7.3 Theme 输入数据

Home Server 向 Theme 写入一组稳定 JSON 文件。

```text
<data>/cache/theme-input/
  site.json
  navigation.json
  post-categories.json
  posts.json
  pages.json
  works.json
  notes.json
  friends.json
  photos.json
  theme-context.json
  build-context.json
```

约束：

- JSON 字段应尽量稳定，新增字段保持向后兼容。
- 内容正文可以同时提供 `markdown`、`html` 和 `excerpt`，由 Theme 选择使用。
- Post / Page / Work 的 canonical route 使用 `siteRelativeUrl`；`url` 暂时保留为 v1 兼容别名。Theme 不应从 slug/title 自己推导内容 URL。
- Post / Page / Work 同时输出绝对 `canonicalUrl`；多语言内容的 `localization.alternates[]` 输出 `language`、`hreflang`、站点根相对 `url` 和绝对 `href`，供 Theme 与 Sitemap 直接生成 SEO alternate link。
- Theme Contract 中的内部 route 和媒体路径保持站点根相对；具体 HTML 输出可以按当前页面位置改写为相对 URL，以支持同一份静态文件部署在域名根目录或二级路径。
- 媒体路径统一转换为站点输出路径，不暴露本机绝对路径。
- `navigation.json` 输出 Menu tree。可链接节点包含 `id`、`label`、解析后的 `href`、原始 `target`、可选 `labelI18n` 和 `children`；无 target 分组节点使用 `href: null` 且 `target: null`，无 target 叶子和无法解析 target 的节点不会进入公开 Theme input。
- `post-categories.json` 输出 Post Category tree，包含稳定 `slug`、`url`、`count` 和 `children`。Post Category URL 固定为 `/posts/categories/{slug}/`。
- `posts.json` 的 Post 节点包含 `categorySlug`，供 Theme 使用稳定分类 URL；`pages.json` 的 Page 节点包含 `template`，供 Theme 选择 Page 模板。
- `build-context.json` 提供构建时间、站点 base URL、环境信息和功能开关。
- `theme-context.json` 是 Theme 的全局上下文，聚合 Bocchi 版本、站点版本/配置、构建信息、作者信息、功能开关和当前 Theme 的合并后有效配置；`theme-context.build.mode` 使用 `full` / `live` 标记普通构建与 Home Server 实时预览。
- Theme 原始用户配置保存到 `state/theme-config/{themeId}.json`；Generator 将其与 `config-schema.json` 默认值合并后写入 `theme-context.theme.config`。
- `theme-context.theme.pageTemplates` 和 `theme-context.theme.specialPages` 暴露当前 Theme 的有效 Page contract。Home Server 会补齐 `normal` template fallback，并过滤无效 special page route。

### 7.4 config-schema.json

Theme 可以声明自己的配置 schema，Home Server 根据 schema 自动生成配置界面。

建议支持的字段类型：

- `string`
- `number`
- `boolean`
- `select`
- `multiSelect`
- `color`
- `image`
- `url`
- `group`

字段可以声明 `description`、`placeholder`、`default`、`required`、`helpText`。`string` / `localizedText` 字段可以声明 `textFormat: "inlineColor"`，表示 Theme 渲染该字段时允许受控的 `[color=#RRGGBB]...[/color]` 或 `[color=accent]...[/color]` 表现层标记；未声明时按 plain text 处理。条件显示使用结构化 `visibleWhen` predicate，支持 `key` + `equals` / `notEquals` / `in`，以及 `all` / `any` 组合；不允许任意 JS 表达式。

示例：

```json
{
  "schemaVersion": "1.0",
  "groups": [
    {
      "id": "home",
      "title": "首页",
      "fields": [
        {
          "key": "home.showWorks",
          "type": "boolean",
          "title": "显示作品模块",
          "description": "在首页展示精选作品。",
          "default": true
        },
        {
          "key": "home.worksTitle",
          "type": "string",
          "title": "作品模块标题",
          "textFormat": "inlineColor",
          "placeholder": "Works",
          "default": "Works"
        },
        {
          "key": "home.heroImage",
          "type": "image",
          "title": "首页图片",
          "accept": ["image/jpeg", "image/png", "image/webp"],
          "recommendedSize": { "width": 1600, "height": 900 },
          "visibleWhen": {
            "key": "home.showHeroImage",
            "equals": true
          }
        }
      ]
    }
  ]
}
```

Theme 配置值建议保存到 `state/theme-config/{themeId}.json`，SQLite 可以缓存最新值和校验结果。

### Theme Package inspection 与安装

Admin Dashboard 上传 zip 包时先写入 `<data>/cache/theme-upload/<inspection-id>/`，服务端 inspection 通过后才允许安装到 `<data>/themes/<theme-id>/`。

Package inspection 支持两种根结构：

- zip 根目录直接包含 `theme.json`。
- zip 内只有一个顶层目录，并且该目录包含 `theme.json`。

Inspection 会阻断目录穿越、绝对路径、Windows drive path、空路径、非法 Theme id、不支持的 Theme Contract、不支持的 runner、非法 `staticAssets` 和坏 JSON；`.git/`、`node_modules/`、既有 build/dist 输出和系统隐藏文件会作为 warning 暴露给 Admin。`config-schema.json` 可选，存在时必须能按 Theme Config Schema 解析。

安装目标始终是 `<data>/themes/<theme-id>/`。更新已有 Theme 时先把旧目录移动到 `<data>/themes/.backups/<theme-id>/<timestamp>/`，再把 staging 目录移动为新的 installed Theme；失败时尝试回滚旧目录。`state/theme-config/<theme-id>.json` 不随 Theme 文件更新而删除。

`default-static` 是内置参考实现，不能通过 zip 覆盖。若要基于默认 Theme 修改，应复制目录并使用新的 Theme id。

### 7.5 Runner 边界

Runner 是 Theme Contract 的执行层，不代表具体技术栈。所有 runner 都遵守同一组环境变量和输出约束：

- `BOCCHI_INPUT_DIR`：Generator 写好的 Theme 输入目录。
- `BOCCHI_OUTPUT_DIR`：Theme 本地输出目录，即 `theme.json.outputDir` 的绝对路径。
- `BOCCHI_THEME_ID`：当前 Theme id。
- `BOCCHI_BASE_URL`：站点 base URL。
- `BOCCHI_ENVIRONMENT`：`development` / `production` 等构建环境标记。

约束：

- Theme 不直接写最终 `output/public/`。
- Home Server Live Preview 是正式构建模式：Theme 仍然只读取 `BOCCHI_INPUT_DIR`、只写 `BOCCHI_OUTPUT_DIR`，只是输入/输出目录由 Home Server 放到一次性 cache 目录。
- `staticAssets` 也写入 `BOCCHI_OUTPUT_DIR`，随后和 runner 输出一起被收集；它不能绕过 artifact manifest。
- Generator 在 runner 结束后统一扫描 Theme 本地输出，登记为 `ArtifactKind.ThemeOutput`，再写入 `output/public/` 和 `.bocchi-manifest.json`。
- stdout / stderr 统一进入 Build 日志。
- timeout、取消、非零退出码统一映射为 Theme runner 错误。
- 普通 Preview / Full Build 不自动执行 `installCommand`；Theme 作者或部署者需要在 Theme repo / 宿主环境中自行准备依赖。
- zip 上传、inspection 和安装阶段不执行 Theme 代码。`process` runner 本质上允许 Theme 在 Home Server 宿主内执行命令，必须在 Dashboard 中显式确认信任。

MVP runner：

- `fluid-static`：调用 Bocchi 随包分发的 Fluid 静态站点 renderer；默认 Theme 与遵守同一模板模型的第三方 Theme 都可以使用。
- `process`：执行本机命令，服务 SvelteKit、Astro、Hugo、自定义 binary、Blazor/Razor static renderer 等自由 Theme。

后续 runner：

- `github-actions`：远端完整构建 / 发布管线，放入 M7 规划。
- `container`：本机容器隔离 runner，适合高级用户和复杂依赖 Theme。

## 8. 默认 Theme 策略

默认 Theme 先使用 Fluid/Liquid 风格模板 renderer，原因是它能在 Home Server Docker 镜像中用较小边际依赖完成真实静态输出，同时给新手和社区一个可读、可修改、受约束的 Theme 起点。默认 Theme 的 canonical source 位于仓库 `Themes/default-static/`，发布时作为 embedded resources 随 Home Server 分发，运行时物化到 `<data>/themes/default-static/`。

MVP 要求：

- 静态输出。
- 首页。
- 文章列表与文章详情。
- 页面详情。
- 作品列表与作品详情。
- 短文列表。
- 友链页。
- RSS / Sitemap 链接；搜索入口等 M7 静态搜索索引存在后再显示。
- 原生渐进增强脚本；首个内置能力是作者/访问者双时区时间提示。

SvelteKit 仍然是推荐的高级 Theme 技术栈之一，尤其适合后续短文 / 推文动态化、Cloud JSON 拉取、复杂交互和 Mastodon 生态对接。后续如果动态能力增加，可以新增 SvelteKit Theme 或切换到 Cloudflare Pages/Workers 相关部署方式，但不改变 Theme Contract 的基础输入。

## 9. 模板与渲染策略

Bocchi 不把某一种模板引擎作为所有 Theme 的中心。

推荐原则：

- Home Server 后台 UI 使用 Blazor Web App + InteractiveServer；Identity 登录、外部登录回调和账号管理走 ASP.NET Core Identity 推荐的服务端端点。
- Page Frontend 通过 Theme Contract 接入，不绑定 Razor、SvelteKit 或任何单一技术。
- RSS、Sitemap、简单文本产物可以使用 .NET 侧轻量模板或直接结构化生成。
- 默认 Theme 源文件与第三方 Theme 使用同一目录形态；`Bocchi.Theme.DefaultStatic` 只承载 `fluid-static` runner、资源物化和少量模板模型代码，不读取数据库或 Home Server UI 状态。
- Razor Components、RazorLight、Fluid/Liquid、SvelteKit、Blazor static renderer 都可以作为后续 Theme adapter；它们不替代整个 Theme Contract。
- 默认 Theme 的前端脚本遵循 progressive enhancement：静态 HTML 先完整可用，再用原生 ES module、`data-*` 和少量 Web Components 增强。

## 10. 发布目标

MVP 发布目标：

- Local Directory：输出到指定本地目录。
- Cloudflare Pages：先以目录产物为目标，后续再接入自动上传或 Git 集成。

后续发布目标：

- GitHub Pages。
- Vercel。
- GitHub Actions Remote Runner：远端完整构建并上传 artifact 或直接部署。
- 自定义 rsync/SFTP。
- 自定义命令。

发布流程应记录：

- 发布目标。
- 输出目录。
- 构建版本或 manifest hash。
- 发布时间。
- 成功/失败状态。
- 错误日志。

## 11. 搜索策略

MVP 搜索优先使用静态 search index。

可选路线：

- 生成简化 JSON 索引，由前端完成本地搜索。
- 使用 Pagefind 等静态站点搜索工具。
- 将来接入 Cloud Server 做动态搜索。

默认建议从静态 JSON 索引开始，等内容量或体验要求提高后再替换实现。

## 12. 安全边界

Home Server 默认运行在可信内网或本机环境。

基础要求：

- 不默认暴露公网端口。
- 默认 fallback authorization policy 要求登录；Setup、Login、外部登录回调和必要登录页资产显式允许匿名。
- Dashboard 操作统一要求 `Admin` role。
- Home Server 内的 `/` 前台预览同样需要登录，预览资源通过受保护端点返回。
- 发布动作需要明确确认或配置。
- Theme 构建命令等同于执行本机代码，只允许安装和运行可信 Theme。
- 后台应提示 Theme 权限风险。
- 媒体路径和输出路径需要做路径穿越防护。
- 发布目标凭据不能写入内容目录。
- GitHub Actions Remote Runner 需要的 token、repo、workflow ref 属于发布凭据，只能放在 Home Server 受保护设置或环境变量中。

## 13. 开放问题

- 默认搜索方案使用自研 JSON index 还是 Pagefind。
- Cloudflare Pages 发布是先走本地目录手动部署，还是较早接入自动化。
- 默认 Theme 是否支持评论占位，以及评论数据由 Cloud Server 还是第三方系统提供。
- workspace 作为 Git 仓库时与远程（GitHub 等）的接入策略：在 M7 发布管线中一起设计。
- GitHub Actions Remote Runner 的最小闭环：触发完整 Bocchi build、轮询状态、读取 artifact，还是先只负责远端发布。

## 14. 架构护栏

后续实现时保持以下约束：

- Home Server 不直接成为对外页面的数据 API。
- Theme 可以自由实现，但必须遵守 Theme Contract。
- 内容正文优先落在可读文件中。
- SQLite 只保存管理状态、索引和缓存。
- Cloud Server 没有明确动态需求前只预留，不提前复杂化。
- Full Build 先跑通，再做 Incremental Build。
- 默认 Theme 先服务真实个人主页体验，不做营销页式空壳。
