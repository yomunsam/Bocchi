# M6 Frontend Menu v1 与 Theme Page Contract

本文记录 Frontend Menu v1、Theme Page template、Theme special page、Post Category slug 和默认 Theme 消费方式。本轮目标是把前台 Menu 从占位能力推进到正式可用的站点级能力，并把关键 Theme 契约写入可恢复的文档。

## 1. 范围

- 前台 Menu 是单个站点级 `primary menu`，由 Dashboard `/Admin/Site/Navigation` 管理。
- Theme 只接收 Menu tree 和解析后的链接，自行决定桌面端、移动端和嵌套层级如何展示。
- `workspace/site/navigation.yaml` 与 `cache/theme-input/navigation.json` 文件名保留，内容升级为正式 Menu tree。
- 自定义 Page 可以选择 Theme 声明的 Page template；Theme special page 可以作为 Menu target，但不由 Home Server 生成 Markdown 内容。
- Post Category 使用稳定 `slug` 作为公开 URL 的依据。

## 2. Menu 数据模型

`workspace/site/navigation.yaml` 使用树形结构：

```yaml
items:
  - id: home
    label: i18n://common@menu.home
    target:
      type: builtin
      value: home
    children: []
```

Menu item 字段：

- `id`：稳定节点 id，由 Dashboard 生成和保留。
- `label`：用户可编辑展示值，可以是普通文本或 `i18n://` 显示值引用。
- `target`：可选语义目标，不直接保存 URL；省略时表示 Dashboard 待配置项，只有存在可输出子项时才作为前台分组节点输出。
- `children`：子项列表，最大深度为 5 层，与 Category tree 保持一致。

Target 类型：

- `builtin`：内置页面，首批为 `home`、`posts`、`works`、`notes`、`friends`。
- `themePage`：当前 Theme manifest 声明的特殊页面。
- `page`：workspace 中的自定义 Page，值为 Page slug。
- `postCategory`：Post Category，值为稳定 slug。

无法解析的 target 在 Dashboard 中保留并显示警告；Generator 写公开 Theme input 时跳过该节点，避免输出死链接。

## 3. Theme Page Contract

Theme manifest 可声明：

```json
{
  "pageTemplates": [
    {
      "name": "normal",
      "displayName": "i18n://theme@theme.defaultStatic.pageTemplate.normal"
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

约定：

- `pageTemplates[]` 字段为 `name` 和 `displayName`。`normal` 强制存在；Theme 未声明时 Home Server 合成 fallback。
- Page frontmatter 可保存 `template`，默认 `normal`；只保存 template name，不保存 Theme id 或关联记录。
- 当前 Page 保存的 template 在 active Theme 中不可用时，编辑器仍显示原 name，并给出 warning。
- `specialPages[]` 字段为 `name`、`displayName` 和 `route`。`route` 必须是站点根相对路径，例如 `/weather/`。
- Home Server 不为 special page 生成内容，只允许 Menu 指向它。

## 4. 显示值 i18n

单字段显示值使用以下形式表达 i18n 引用：

```text
i18n://common@menu.home
i18n://theme@theme.defaultStatic.pageTemplate.normal
```

`common@` 指向 Bocchi 通用前台文案；`theme@` 指向当前 Theme manifest 提供的私有文案。本轮只在 Menu label、Theme manifest Page template / special page `displayName` 和 Dashboard 展示中解析该约定，不把它扩展为全字段全局机制。

## 5. Theme Input

Generator 写入：

- `navigation.json`：嵌套 Menu tree，包含 `id`、`label`、解析后的 `href`、原始 `target`、可选 `labelI18n` 和 `children`；无 target 分组使用 `href: null`，无 target 叶子不进入公开 input。
- `post-categories.json`：Post Category tree，包含 `id`、`name`、`slug`、`url`、`count` 和 `children`。
- `posts.json`：Post 增加 `categorySlug`。
- `pages.json`：Page 增加 `template`。
- `theme-context.json`：当前 Theme 的有效 `pageTemplates` 与 `specialPages`。

构建指纹纳入 Menu、Category slug、Post `categorySlug`、Page `template`、Post Category tree 和 Theme Page contract，保证这些展示契约变化会触发重新构建。

## 6. Dashboard

`/Admin/Site/Navigation` 提供正式编辑器：

- 添加根项和子项。
- 编辑 label。
- 从下拉选择 target。
- 支持无 target 待配置项，并与 unavailable target warning 区分。
- 上移、下移、删除节点。
- 保存嵌套 Menu tree。
- 对 unavailable target 给出 warning 并保留原值。

Page 创建/编辑：

- `/Admin/Content/Edit?kind=page` 支持新建 Page。
- Page 编辑侧栏显示当前 active Theme 的 template 下拉。
- unavailable template 显示 warning，保存时继续只写原 template name。

Category editor：

- Post Category 节点显示 slug 输入。
- 新节点可由 name 自动生成 slug。
- 已有 slug 不随 rename 自动改变。
- 保存时保证同一 Post category tree 内 slug 非空且唯一。

## 7. 默认 Theme

`default-static` 不再硬编码顶栏导航，而是消费 `navigation.json`。桌面端和移动端使用同一 Menu tree，递归渲染嵌套节点。

默认 Theme 同时输出 Post Category 列表页：

```text
/posts/categories/{slug}/
```

Page 渲染优先使用 `standalone-page-{template}.liquid`；缺失时回退 `standalone-page.liquid`。

## 8. 验收

- Workspace tests 覆盖 Page template 读取默认值、Menu YAML 解析和嵌套保存。
- GeneratorContract tests 覆盖 Theme manifest 的 Page templates、special pages 和显示值引用解析。
- Generator tests 覆盖 `navigation.json`、`post-categories.json`、`posts[].categorySlug`、Category page 输出和构建指纹。
- HomeServer tests 覆盖 Navigation editor、Page template selector、unknown template warning 和 Category slug 保存。
- 视觉 smoke 应覆盖 `/Admin/Site/Navigation`、Page editor template selector、Preview root、Post Category page 和移动端导航。

2026-05-23 验证记录：

- `dotnet test Tests/Bocchi.Workspace.Tests/Bocchi.Workspace.Tests.csproj --no-restore --disable-build-servers -v:minimal /m:1 /nr:false`
- `dotnet test Tests/Bocchi.GeneratorContract.Tests/Bocchi.GeneratorContract.Tests.csproj --no-restore --disable-build-servers -v:minimal /m:1 /nr:false`
- `dotnet test Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj --no-restore --disable-build-servers -v:minimal /m:1 /nr:false`
- `dotnet test Tests/Bocchi.HomeServer.Tests/Bocchi.HomeServer.Tests.csproj --no-restore --disable-build-servers -v:minimal /m:1 /nr:false`
- `dotnet test Bocchi.slnx --no-restore --disable-build-servers -v:minimal /m:1 /nr:false`
- `git diff --check`
- `jq empty Src/HomeServer/Bocchi.HomeServer/Localization/Dashboard/zh-CN.json Src/HomeServer/Bocchi.HomeServer/Localization/Dashboard/en-US.json`
- Browser smoke：临时 DataRoot Setup 后，验证 `/Admin/Site/Navigation`、Page editor template selector、Preview root、`/posts/categories/smoke/` 和 `412x915` 移动端导航。截图采集受 in-app Browser 当前 CDP 截图超时影响未留档，但 DOM、URL、title 和服务器日志均确认页面成功渲染。
