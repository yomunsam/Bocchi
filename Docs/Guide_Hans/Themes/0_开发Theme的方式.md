# 开发 Theme 的方式

Bocchi Theme 是一个可以独立开发、安装和更新的前台静态站点外观包。一个 Theme 应该只负责公开站点的页面结构、样式、前端交互和 Theme 自有资源；Dashboard、账户、安装向导等管理界面不属于 Theme 覆盖范围。

本文介绍当前推荐的第三方 Theme 开发方式：维护一个独立的 Theme Root，使用 `theme.json` 声明 Theme Contract，通过 `fluid-static` Runner 渲染 Liquid 模板，并用 Dev Link 接入本地 Bocchi 实例调试。完成后，Theme 以 zip 包形式安装或更新到正式实例。

## 基本概念

- `Theme Root`：Theme 的根目录，必须直接包含 `theme.json`。
- `theme.json`：Theme manifest，用来声明 Theme id、展示名称、版本、Contract 版本、Runner、功能开关、页面模板、i18n key 和静态资源复制规则。
- `config-schema.json`：Theme 暴露给 Dashboard 的配置结构。只有需要让站点管理员调整的 Theme 设置才需要放在这里。
- `templates/`：模板目录。`fluid-static` Theme 使用 Liquid 模板。
- `assets/`：Theme 自带的 CSS、JavaScript、图片、字体、图标库等资源。
- `staticAssets`：`theme.json` 中的静态资源复制规则。Bocchi 只允许它从 Theme Root 内复制到站点输出目录内。
- `Dev Link`：开发期把 DataRoot 下的 Theme id 映射到一个外部绝对路径，让 Bocchi 直接读取本地 Theme Root。
- `Theme Package`：用于安装或更新的 zip 包。包根目录可以直接包含 `theme.json`，也可以只有一个顶层目录且该目录包含 `theme.json`。

## 目录结构

推荐使用清晰、可直接打包的目录结构：

```text
my-theme/
  theme.json
  config-schema.json
  README.md
  templates/
    layouts/
      base.liquid
    pages/
      index.liquid
      posts.liquid
      works.liquid
      notes.liquid
      friends.liquid
      article.liquid
      standalone-page.liquid
      404.liquid
  assets/
    app.css
    app.js
    images/
    fonts/
    vendor/
  build/
```

`build/` 是 Theme 构建产物目录，不应提交到源码仓库，也不应放进安装包。

## Manifest

最小 `fluid-static` Theme manifest 示例：

```json
{
  "id": "my-theme",
  "name": "My Theme",
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
      "to": "/assets"
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
      "displayName": "i18n://theme@theme.myTheme.pageTemplate.normal"
    }
  ],
  "specialPages": [],
  "i18n": {
    "supportedLanguages": ["en-US", "zh-CN"],
    "defaultLanguage": "en-US",
    "keys": [
      {
        "key": "theme.myTheme.pageTemplate.normal",
        "title": "Normal page template name",
        "description": "Dashboard label for the default standalone page template.",
        "defaultValues": {
          "en-US": "Normal",
          "zh-CN": "普通页面"
        }
      }
    ]
  }
}
```

关键字段说明：

- `id`：Theme 的稳定标识。安装、更新、配置保存和 Dev Link 都以它为准。
- `version`：Theme 自身版本。发布新包时应递增。
- `contractVersion`：Theme 兼容的 Bocchi Theme Contract 版本。
- `inputDir`：Theme 输入数据目录，通常保持默认值 `../../cache/theme-input`。
- `outputDir`：Theme 构建产物目录，通常保持默认值 `build`。
- `runner.kind`：`fluid-static` 表示使用 Bocchi 内置静态模板渲染器。
- `staticAssets`：声明需要原样复制到输出站点的资源目录。
- `features`：声明 Theme 支持展示的内容类型。
- `pageTemplates`：声明 Dashboard 中可选的独立页面模板。
- `i18n.keys`：声明 Theme 自有文案。模板和客户端脚本中的可见文案应通过 Theme i18n 提供默认值。

## Liquid 模板

`fluid-static` Runner 会读取 `templates/pages/` 下的页面模板，并用 Bocchi 提供的 Theme 输入数据渲染静态 HTML。

常用页面模板：

- `index.liquid`：首页。
- `posts.liquid`：文章列表页。
- `article.liquid`：文章详情页。
- `standalone-page.liquid`：独立页面默认模板。
- `standalone-page-{template}.liquid`：独立页面的自定义模板，`{template}` 对应 `pageTemplates[].name`。
- `works.liquid`：作品列表页。
- `notes.liquid`：短文列表页。
- `friends.liquid`：友链页。
- `404.liquid`：404 页面。

模板可以通过 `templates/layouts/base.liquid` 复用全站 HTML 外壳。公共片段可以放在 `templates/partials/`，由 Theme 自己组织。

## 资源规则

Theme 输出的 HTML、CSS 和 JavaScript 不应依赖远程 CDN。运行时需要的 CSS 库、图标库、脚本库、图片和 web font 应放在 Theme Root 内，并通过 `staticAssets` 复制到输出站点。

字体需要同时考虑视觉效果和安装包大小。Bocchi 默认 Theme Package 限制为 zip 总大小 50MB、文件数量 2000、单个文件 10MB。CJK 字体尤其容易超过限制，Theme 作者可以选择以下方式：

- 使用系统字体 fallback，不打包 CJK web font。
- 只打包必要字重和必要字符范围的 woff2 文件。
- 对标题、正文、代码分别选择更小的字体集。

无论采用哪种方式，都应确保 CSS 中引用的字体文件实际存在于 Theme 包内，或明确使用系统字体族作为 fallback。

## 本地开发

Theme 可以放在任意本地目录中开发。开发时，在 Bocchi DataRoot 下创建 `themes/dev-links.json`：

```json
{
  "schemaVersion": "1.0",
  "links": [
    {
      "id": "my-theme",
      "root": "/absolute/path/to/my-theme",
      "enabled": true,
      "note": "Local theme development"
    }
  ]
}
```

要求：

- `root` 必须是绝对路径。
- `id` 必须与 Theme Root 中 `theme.json.id` 一致。
- `dev-links.json` 属于 DataRoot 运行数据，不属于站点 workspace，也不应进入 Theme 包。
- Development 环境默认启用 Dev Link；Production 环境默认忽略 Dev Link，除非显式启用 `Bocchi:Themes:AllowDevLinks=true`。

在 Bocchi 源码仓库根目录执行一次构建：

```bash
dotnet run --project Src/HomeServer/Bocchi.HomeServer -- build --theme=my-theme --env=Development --include-drafts
```

构建成功后，输出位于当前 DataRoot 的 `output/public/`。如果需要指定 DataRoot，可以通过 Bocchi 配置设置 `Bocchi:DataRoot`，或使用当前开发实例已经配置好的 DataRoot。

## 调试清单

每次较大改动后，建议至少检查：

1. `theme.json` 可以被 Bocchi 解析，Theme id 与 Dev Link id 一致。
2. 构建产物中存在首页、列表页、详情页、独立页面和 404 页面。
3. `assets/` 中声明的 CSS、JavaScript、图片、字体和 vendor 文件都被复制到输出目录。
4. 输出 HTML、CSS、JavaScript 中没有远程 CDN URL。
5. 页面模板在空列表、无封面图、无摘要、多语言内容、未发布内容等状态下不会报错。
6. 语言切换、外观切换、导航菜单和正文链接在本地预览中可用。
7. 删除或重命名静态资源后，模板和 CSS 中不再引用旧路径。

## 打包

发布 Theme 时，把 Theme Root 打成 zip。zip 可以使用以下两种结构之一：

```text
theme.json
templates/
assets/
```

或：

```text
my-theme/
  theme.json
  templates/
  assets/
```

打包前应排除：

- `.git/`
- `build/`
- `dist/`
- `node_modules/`
- 下载缓存和临时脚本
- `.DS_Store` 等系统文件

Bocchi 安装 Theme Package 时会先 inspection。包含旧构建产物、隐藏系统文件或无关依赖目录的包可能会产生 warning；缺失 `theme.json`、Theme id 非法、manifest 无法解析、资源路径越界等问题会阻止安装。

## 更新

同 id Theme 的新 zip 会被视为完整替换包。Theme 作者发布更新时应保证：

- `theme.json.id` 保持不变。
- `version` 递增。
- 新包包含运行所需的完整模板和资源。
- 删除的静态资源不再被模板、CSS 或 JavaScript 引用。
- 仍然保留必要的 `config-schema.json` 默认值和 Theme i18n 默认值。
- 破坏性配置变更应在发布说明中写明迁移方式。

更新验证应使用 zip 安装形态重新构建一次。Dev Link 构建通过只能说明本地 Theme Root 可用，不能替代安装包验证。
