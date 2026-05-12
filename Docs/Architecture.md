# Bocchi Architecture

本文档描述 Bocchi 的目标架构、模块边界、数据契约和近期实现原则。它以 `README.md` 中的初步构想为准，将项目收束为一个“内网内容工作台 + 静态站点生成器 + 可选云端运行时”的个人主页/Blog 系统。

## 1. 产品定位

Bocchi 是个人自用的主页与 Blog 系统。它优先服务以下目标：

- 个人内容长期可控，Markdown 文件和媒体文件应当能脱离系统直接保存与迁移。
- 对外页面优先静态化，适合发布到本地目录、Cloudflare Pages、GitHub Pages、Vercel 等托管环境。
- 后台管理只运行在 NAS、个人服务器或本机内网环境，不作为公网业务 API 暴露。
- 前端 Theme 技术栈应保持开放，Home Server 只约束输入/输出契约，不强行绑定某个前端框架。

非目标：

- MVP 阶段不做多用户协作 CMS。
- MVP 阶段不把 Home Server 设计成公网 API Server。
- MVP 阶段不要求所有内容都进数据库，文件仍然是内容事实来源。
- Cloud Server 只做预留，不在没有明确动态需求前提前实现复杂后端。

## 2. 系统组成

### 2.1 Home Server

Home Server 是 Bocchi 的内容管理系统、Dashboard 和静态站点构建器。

职责：

- 管理文章、页面、作品、短文、友链、站点设置和预留照片墙内容。
- 扫描并解析内容工作区中的 Markdown、frontmatter 和媒体文件。
- 维护少量 SQLite 元数据，例如构建缓存、发布历史、后台界面状态、主题配置和内容索引。
- 生成标准化内容数据，传递给 Theme。
- 调用 Theme 构建流程，产出可部署的静态站点目录。
- 生成 RSS、Sitemap、搜索索引等站点级产物。
- 管理发布目标，例如本地输出目录和 Cloudflare Pages 相关流程。

技术方向：

- 基于 .NET 10 / ASP.NET Core。
- 后台 UI 可以使用 Razor Pages、Blazor 或其他 ASP.NET Core 友好的方案。
- Markdown 解析建议优先使用成熟 .NET Markdown 处理库。
- SQLite 只承担管理与索引职责，不替代 Markdown 文件作为内容源。

### 2.2 Page Frontend

Page Frontend 是对外访问的个人主页与 Blog 页面。它由 Theme 提供实现。

职责：

- 根据 Home Server 生成的标准化数据渲染首页、文章、页面、作品、短文、友链等页面。
- 输出静态资源和页面文件。
- 支持 RSS、Sitemap 和搜索索引的消费或展示。
- 在将来需要评论、动态短文、统计等功能时，能够接入 Cloud Server。

默认方向：

- 默认 Theme 使用 SvelteKit，并以静态生成为第一目标。
- 允许其他 Theme 使用 Astro、Next static export、纯 HTML 模板、Razor renderer 或任意可执行构建流程。
- Home Server 不直接干涉 Theme 的内部技术栈，只读取描述文件、写入输入数据、执行构建命令并校验输出。

### 2.3 Cloud Server

Cloud Server 是可选组件，用于承载必须在线运行的少量功能。

可能职责：

- 评论系统。
- 动态短文发布或同步。
- Webmention、订阅回调、轻量统计。
- 需要权限控制或边缘函数的动态 API。

MVP 阶段只保留接口边界和目录预留，不实现具体服务。

## 3. 内容工作区

建议将用户内容和系统缓存分开。一个 Bocchi workspace 可以采用如下结构：

```text
Workspace/
  content/
    posts/
    pages/
    works/
    notes/
    friends/
    photos/
  media/
    images/
    videos/
    files/
  themes/
    default-svelte/
  site/
    site.json
    navigation.json
  .bocchi/
    bocchi.sqlite
    build-manifest.json
    publish-history.json
    theme-config/
  output/
    public/
```

原则：

- `content/` 与 `media/` 是用户可直接维护、可备份、可迁移的内容事实来源。
- `.bocchi/` 是 Bocchi 管理状态，允许重建，不应成为唯一内容事实。
- `output/` 是生成产物，可以清理后重新构建。
- `themes/` 可以存放内置 Theme、副本 Theme 或用户自定义 Theme。

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

字段建议：

- `id` 或 `slug`
- `status`
- `publishedAt`
- `text`
- `media`
- `tags`

短文可以在 MVP 中先以 Markdown 文件或 JSON Lines 文件表示。若后续发布频率较高，可再引入更适合追加写入的存储形式。

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

### 4.7 Photo

照片墙预留。MVP 可以只定义字段，不强制实现完整 UI。

字段建议：

- `title`
- `takenAt`
- `location`
- `media`
- `albums`
- `tags`
- `description`

## 5. SQLite 职责

SQLite 用于 Home Server 的管理状态和派生索引，不作为内容唯一事实源。

适合进入 SQLite 的内容：

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

### 7.1 Theme 目录结构

```text
Theme/
  theme.json
  config-schema.json
  package.json
  src/
  static/
```

非 Node.js Theme 可以没有 `package.json`，但必须在 `theme.json` 中声明构建命令。

### 7.2 theme.json

`theme.json` 描述 Theme 元信息和构建方式。

```json
{
  "id": "default-svelte",
  "name": "Default Svelte Theme",
  "version": "0.1.0",
  "contractVersion": "1.0",
  "inputDir": ".bocchi/input",
  "outputDir": "build",
  "build": {
    "command": "pnpm build",
    "installCommand": "pnpm install"
  },
  "features": {
    "posts": true,
    "pages": true,
    "works": true,
    "notes": true,
    "friends": true,
    "photos": false,
    "search": true
  }
}
```

### 7.3 Theme 输入数据

Home Server 向 Theme 写入一组稳定 JSON 文件。

```text
.bocchi/input/
  site.json
  navigation.json
  posts.json
  pages.json
  works.json
  notes.json
  friends.json
  photos.json
  theme-config.json
  build-context.json
```

约束：

- JSON 字段应尽量稳定，新增字段保持向后兼容。
- 内容正文可以同时提供 `markdown`、`html` 和 `excerpt`，由 Theme 选择使用。
- 媒体路径统一转换为站点输出路径，不暴露本机绝对路径。
- `build-context.json` 提供构建时间、站点 base URL、环境信息和功能开关。

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

示例：

```json
{
  "groups": [
    {
      "id": "home",
      "title": "首页",
      "fields": [
        {
          "key": "home.showWorks",
          "type": "boolean",
          "title": "显示作品模块",
          "default": true
        },
        {
          "key": "home.worksTitle",
          "type": "string",
          "title": "作品模块标题",
          "default": "Works"
        }
      ]
    }
  ]
}
```

Theme 配置值建议保存到 `.bocchi/theme-config/{themeId}.json`，SQLite 可以缓存最新值和校验结果。

## 8. 默认 Theme 策略

默认 Theme 使用 SvelteKit，原因是它既能做静态站点，也能在未来接入少量动态能力。

MVP 要求：

- 静态输出。
- 首页。
- 文章列表与文章详情。
- 页面详情。
- 作品列表与作品详情。
- 短文列表。
- 友链页。
- RSS、Sitemap、搜索索引入口。

后续如果动态能力增加，可以切换到 Cloudflare Pages/Workers 相关部署方式，但不改变 Theme Contract 的基础输入。

## 9. 模板与渲染策略

Bocchi 不把某一种模板引擎作为所有 Theme 的中心。

推荐原则：

- Home Server 后台 UI 使用 ASP.NET Core 生态内合适的 UI 技术。
- Page Frontend 通过 Theme Contract 接入，不绑定 Razor、SvelteKit 或任何单一技术。
- RSS、Sitemap、简单文本产物可以使用 .NET 侧轻量模板或直接结构化生成。
- 如果未来需要 `.NET Native Theme`，可以把 Razor Components 静态渲染作为一个 Theme adapter，而不是替代整个 Theme Contract。

## 10. 发布目标

MVP 发布目标：

- Local Directory：输出到指定本地目录。
- Cloudflare Pages：先以目录产物为目标，后续再接入自动上传或 Git 集成。

后续发布目标：

- GitHub Pages。
- Vercel。
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
- 发布动作需要明确确认或配置。
- Theme 构建命令等同于执行本机代码，只允许安装和运行可信 Theme。
- 后台应提示 Theme 权限风险。
- 媒体路径和输出路径需要做路径穿越防护。
- 发布目标凭据不能写入内容目录。

## 13. 开放问题

- Home Server 后台 UI 使用 Razor Pages、Blazor 还是混合方案。
- Markdown frontmatter 采用 YAML、TOML 还是同时兼容。
- 短文的初始存储格式使用 Markdown 文件、JSON 文件还是 SQLite + 导出。
- 默认搜索方案使用自研 JSON index 还是 Pagefind。
- Cloudflare Pages 发布是先走本地目录手动部署，还是较早接入自动化。
- 默认 Theme 是否支持评论占位，以及评论数据由 Cloud Server 还是第三方系统提供。

## 14. 架构护栏

后续实现时保持以下约束：

- Home Server 不直接成为对外页面的数据 API。
- Theme 可以自由实现，但必须遵守 Theme Contract。
- 内容正文优先落在可读文件中。
- SQLite 只保存管理状态、索引和缓存。
- Cloud Server 没有明确动态需求前只预留，不提前复杂化。
- Full Build 先跑通，再做 Incremental Build。
- 默认 Theme 先服务真实个人主页体验，不做营销页式空壳。
