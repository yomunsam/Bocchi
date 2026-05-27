# M6 Theme 开发专项

本文是 M6 期间补充的 Theme 开发与安装专项规划。它不替代 `M6.md` 的本地化主线，而是把 Theme 作者开发循环、Admin Dashboard 上传安装、Theme 切换与安全边界整理成一组可实施、可验收的工作包。

## 1. 背景

M5 已经把默认 Theme 外置为 `Themes/default-static/`，并在运行时物化到 `<data>/themes/default-static/`。M6 又把 Theme 私有 i18n、Page template、special page、Menu input 和 Live Preview 推进到正式契约。下一步的问题不再只是“Theme 怎么被 Generator 调用”，而是：

1. Theme 作者如何在本机高频开发和调试一个外部 Theme。
2. 普通 Admin 如何在 Dashboard 中安装或更新一个 Theme 包。
3. Home Server 如何在源码运行和 Docker 运行两种宿主方式下保持同一套 Theme Contract。

当前判断：

- Theme 作者的一等开发路径应是本机源码 `dotnet run`。Docker 只作为接近部署环境的验证方式，不作为 Theme 开发的默认前提。
- 开发入口和用户安装入口必须分开。开发使用 Dev Link，用户安装使用 Zip Package。
- Theme 实现、Theme 配置和 Theme 包管理都属于 DataRoot 运行数据，不进入 `<data>/workspace/`。

当前状态（2026-05-25 代码同步）：

- 已落地：`ThemeResolver` / Catalog、Dev Link、Theme Package inspection、Zip 安装 / 更新 / 回滚、Theme Library 页面、active Theme Catalog picker、`process` runner trust 确认，以及 Architecture / Themes README 的主要契约回写。
- 部分落地：Theme 作者开发体验已经能显示 source/root/runner/diagnostics，Build log 也会记录 Theme Root 和 runner；但 Preview 失败在 UI 中还没有按 manifest / runner / output / content 做细分呈现。
- 待补验证：浏览器级 zip 上传安装 smoke、Docker 近似 Dev Link 验证，以及本专项最终验证记录。

M6 收束口径（2026-05-26）：

- 上述待补验证和 Preview 错误 UI 分类继续作为 Theme 开发专项收尾项追踪。
- 它们不阻塞 M6 localization 主线完成；M6-T09 只需要明确记录这些专项残留风险和后续入口。

## 2. 文档结构约定

为了避免“分类详情”和“Todo list”脱节，本文采用同一编号贯穿：

- `TD-xx` 表示一个 Theme Development 工作包。
- 每个 `TD-xx` 小节内同时包含：目标、设计、Todo、验收、测试建议。
- 文末总清单只引用 `TD-xx`，不重新定义另一套任务含义。

## 3. 核心目录与术语

```text
<data>/                              # DataRoot
  workspace/                         # 用户内容，不放 Theme 实现或 Theme 配置
  themes/
    default-static/                  # 已安装 Theme
    my-theme/                        # 已安装第三方 Theme
    dev-links.json                   # 开发期 Theme 外部根目录清单
  state/
    theme-config/
      my-theme.json                  # Theme 实例配置，按 Theme id 隔离
  cache/
    theme-input/                     # Full Build 的 Theme Contract 输入
    theme-upload/                    # Zip 上传检查与解压临时区
  output/
    public/                          # 最终静态输出
```

| 术语 | 含义 |
| --- | --- |
| Theme Root | 包含 `theme.json` 的 Theme 根目录。 |
| Installed Theme | 位于 `<data>/themes/<theme-id>/` 的 Theme。 |
| Dev Link | 位于 `<data>/themes/dev-links.json` 的开发期外部 Theme Root 映射项。 |
| Theme Package | Admin Dashboard 上传的 zip 包，解包后必须得到一个有效 Theme Root。 |
| Active Theme | `SiteProfileSettings.DefaultThemeId` 指向的当前前台 Theme。 |
| Theme Source | Theme 的来源类型：`builtIn`、`installed`、`devLink`、`packageCandidate`。 |

Dev Link 清单形态：

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

Docker 模式下 `root` 使用容器内路径，例如 `/theme-dev/my-theme`；宿主机路径通过 `-v /host/path/my-theme:/theme-dev/my-theme` 映射。

常规 Theme 形态始终保持为 `<data>/themes/<theme-id>/theme.json`。`dev-links.json` 只在开发期提供“theme id 到外部 Theme Root”的映射，不改变 Theme 包和已安装 Theme 的标准目录结构。

## 4. 总体架构

Theme 发现与加载应从“直接拼 `<data>/themes/<id>`”升级为显式 Resolver：

```text
ThemeManifestLoader
  只负责从一个 Theme Root 读取 theme.json。

ThemeResolver / ThemeCatalog
  负责列出 Theme、解析 themeId、合并 installed 与 dev link、返回诊断。

ThemePackageService
  负责 zip 检查、解压、校验、安装、更新和回滚。

ThemeSettingsService / LoadThemeStage / Dashboard
  不再自己拼 Theme Root，统一通过 ThemeResolver。
```

解析优先级：

1. Development 模式且 Dev Link 启用时，`<data>/themes/dev-links.json` 中 enabled 的 link 可以覆盖同 id 的 Installed Theme。
2. Installed Theme 使用 `<data>/themes/<id>/`。
3. `default-static` 在需要时继续由内置资源补齐到 `<data>/themes/default-static/`。
4. 找不到 Theme 时，构建阶段保留现有 warning 行为；Dashboard 需要把不可用状态清楚展示出来。

Dev Link 默认只在 `Development` 环境启用。Production 若要启用，必须显式配置：

```json
{
  "Bocchi": {
    "Themes": {
      "AllowDevLinks": true
    }
  }
}
```

## 5. 工作包

### TD-01 Theme Resolver 与 Catalog 抽象

目标：建立统一 Theme 发现入口，让 Generator、Home Server、Dashboard 不再各自拼路径。

设计：

- 新增 `ThemeResolver` 或等价服务，输入为 `BocchiDataLayout`、环境名和 Theme 开发选项。
- 返回 `ResolvedTheme`，至少包含 `Id`、`Name`、`Version`、`ContractVersion`、`Root`、`SourceKind`、`Manifest`、`Diagnostics`。
- `ListAvailableThemesAsync` 返回 installed 与 enabled dev links 的合并列表。
- `ResolveThemeAsync(themeId)` 负责处理 Dev Link shadow、default-static 物化和 manifest 读取。
- `ThemeManifestLoader` 保持低层职责，只读取指定 root 的 `theme.json`。

Todo：

- [x] 定义 `ThemeSourceKind`、`ResolvedTheme`、`ThemeCatalogItem`、`ThemeDiagnostic`。
- [x] 实现 ThemeResolver，覆盖 installed、default-static、dev link 三类来源。
- [x] 将 `LoadThemeStage` 改为通过 ThemeResolver 解析 Theme。
- [x] 将 `ThemeSettingsService.ListAvailableThemesAsync` 和 Theme 定制读取逻辑改为通过 ThemeResolver。
- [x] 保留找不到 Theme 时 Generator 的 warning 行为，Dashboard 则展示不可用诊断。

验收：

- [x] `default-static` 仍会自动物化并排在 Theme 列表中。
- [x] `<data>/themes/my-theme/theme.json` 可被列表和构建识别。
- [x] 无效 Theme 不让列表崩溃，Dashboard 能看到可解释的诊断。
- [x] Generator 和 Dashboard 对同一个 `themeId` 解析到同一个 Theme Root。

测试建议：

- ThemeResolver 单元测试覆盖 installed Theme、missing Theme、invalid manifest、default-static ensure。
- HomeServer 测试覆盖 Theme 列表不会因单个坏 Theme 中断。
- Generator 测试覆盖 `LoadThemeStage` 通过 resolver 后仍写入同样的 build log。

### TD-02 Dev Link 契约

目标：让 Theme 作者可以把外部 Theme repo 直接挂到 Home Server，不复制、不打包、不进容器。

设计：

- Dev Link 清单位于 `<data>/themes/dev-links.json`。
- Dev Link 只在 Development 环境默认启用；Production 需要 `Bocchi:Themes:AllowDevLinks=true`。
- `dev-links.json` 是单文件清单，避免把开发状态拆散到多个难读的小文件。
- 每个 link 的 `root` 必须是绝对路径。
- 每个 link 的 `id` 必须和 `root/theme.json` 中的 `id` 一致。
- 每个 enabled link 的 `root` 必须存在，且必须包含有效 `theme.json`。
- `enabled=false` 的 link 保留在清单中，但不参与 Theme 解析。
- Dev Link 可 shadow 同 id 的 Installed Theme，但 Dashboard 必须显示该状态。
- Dev Link 不写入 workspace，也不写入 zip 安装目录。

Todo：

- [x] 定义 `ThemeDevLinksManifest` 与 `ThemeDevLinkEntry` JSON 结构和解析错误。
- [x] 实现 `dev-links.json` 读取、enabled 过滤和重复 id 诊断。
- [x] 校验 root 绝对路径、manifest id 一致性、非法 theme id。
- [~] 禁用 Dev Link 时完全忽略 `dev-links.json`，但可在诊断页提示“开发链接未启用”。当前代码已忽略，独立诊断页提示尚未实现。
- [x] 当 Dev Link shadow Installed Theme 时，在 catalog item 上标记 `ShadowsInstalledTheme=true`。

验收：

- [x] 源码 `dotnet run` 时，外部 `/Users/.../my-theme` 可作为 active Theme 被 Preview 使用。
- [x] 修改外部 Theme 的 `.liquid`、CSS 或 `theme.json` 后，刷新 Preview 可看到变化，不需要复制 Theme。
- [~] Docker 模式下，只要 `root` 是容器内挂载路径，同一套 Dev Link 契约可用。契约已文档化，尚缺 Docker 近似验证记录。
- [x] Production 默认不会解析 Dev Link。
- [x] Dev Link root 缺失、id 不一致、manifest 缺失或重复 id 时不会中断 Home Server 启动。

测试建议：

- ThemeResolver 测试覆盖 Dev Link 启用、禁用、shadow、root 缺失、id 不一致、重复 id。
- 使用临时目录构造外部 Theme Root，验证 `ResolveThemeAsync` 返回外部路径。
- 构建指纹测试覆盖外部 Theme Root 文件变化会触发重新构建。

### TD-03 Theme 作者开发体验

目标：把 Theme authoring 变成清楚、低摩擦的本地循环。

设计：

- 官方推荐开发流程是：

```bash
dotnet run --project Src/HomeServer/Bocchi.HomeServer
```

- Theme 作者在任意位置维护 Theme repo，例如 `/Users/yomu/Projects/my-theme`。
- 在当前 DataRoot 创建 Dev Link：

```text
<data>/themes/dev-links.json
```

- Dashboard 显示当前 active Theme 的 source：Installed 或 Dev Link。
- Live Preview 不需要 watch server；第一版以“刷新 Preview 触发一次 Live Build”为闭环。
- `process` runner 的依赖安装不由普通 Preview 自动执行；Theme 作者在自己的 Theme repo 里手动准备依赖。
- Build log 必须显示 Theme Root 与 runner 类型，方便定位路径和命令问题。

Todo：

- [x] 在 `/Admin/Site/Theme` 或 Theme library 页面展示 active Theme source、root、runner、diagnostics。
- [x] 在文档中写明源码运行与 Docker 运行两种开发方式，其中源码运行是推荐路径。
- [x] Build log 增加 Theme source/root 摘要，避免开发者不知道当前到底跑了哪个 Theme。
- [~] Live Preview 失败时，把 Theme runner 错误、manifest 错误、路径错误区分展示。Build log 已保留诊断，UI 细分呈现仍需补。
- [x] 明确 `process` runner 的 installCommand 不会在普通 Preview/Full Build 自动运行。

验收：

- [x] Theme 作者可以只通过 `dotnet run` + Dev Link 完成模板/CSS/manifest/schema 调试。
- [x] Dashboard 能一眼看出 active Theme 来自外部 Dev Link。
- [~] Preview 错误能指出是 Theme manifest、runner command、输出缺失还是内容输入问题。日志路径已具备基础信息，UI 分类仍待补。
- [x] 关闭 Dev Link 后，同 id Installed Theme 或 missing Theme 状态显示正确。

测试建议：

- HomeServer 测试覆盖 Theme 页面渲染 source/diagnostic。
- PreviewEndpoint 测试覆盖 Dev Link Theme 输出 HTML。
- 错误路径测试覆盖 root 不存在和 runner 非零退出码。

### TD-04 Active Theme 选择与切换体验

目标：让 Theme 安装、Dev Link 和 active Theme 切换形成一个连续的 Dashboard 流程。

设计：

- `Settings / Profile` 中的 `DefaultThemeId` 文本框应升级为 Theme 选择控件。
- Theme 选择控件使用 ThemeResolver/Catalog 数据源。
- 列表项展示 Theme name、id、source、version 和 warning 状态。
- 如果选择的新 Theme 会影响 Menu 中的 `i18n://theme@...` 引用，继续走现有 Theme Migration 向导。
- 上传新 Theme 成功后提供“设为当前 Theme”动作；同样复用 Theme Migration。

Todo：

- [x] 将 Settings 中的 Theme id 输入框替换为 select/list picker。
- [x] picker 显示 installed/dev link/source 状态。
- [x] picker 禁止选择 manifest 无效的 Theme。
- [x] 上传安装完成后，允许直接进入“设为当前 Theme”流程。
- [x] 保持 Theme Migration 对 `i18n://theme@...` 的保护。

验收：

- [x] Admin 不需要手动复制 Theme id。
- [x] Dev Link Theme 和 Zip 安装 Theme 都可以从同一个控件选择。
- [x] 无效 Theme 出现在诊断中，但不会被设为 active Theme。
- [x] 切换 Theme 时已有 Menu 迁移逻辑仍然生效。

测试建议：

- HomeServer 页面测试覆盖 Theme picker 渲染和保存。
- ThemeMigrationService 测试覆盖从 installed 切到 dev link、从旧 Theme 切到新 zip Theme。

### TD-05 Zip Package 检查与校验

目标：允许 Admin 上传 zip，但在写入 `<data>/themes/<id>/` 之前完成严格检查。

设计：

- 上传 zip 先写到 `<data>/cache/theme-upload/<upload-id>/`。
- 支持两种 zip root：
  - zip 根目录直接包含 `theme.json`。
  - zip 内只有一个顶层目录，该目录包含 `theme.json`。
- 解压时禁止目录穿越、绝对路径、Windows drive path、空文件名。
- 拒绝或警告 `.git/`、`node_modules/`、现有 build output、隐藏系统文件。
- 必须读取并校验 `theme.json`：
  - `id` 非空且可作为目录名。
  - `contractVersion` 是当前支持版本。
  - `runner.kind` 是 `fluid-static` 或 `process`。
  - `theme.json.id` 与最终安装目录一致。
- `config-schema.json` 可选；存在时必须能解析为 Dashboard 支持的 schema 形态。
- 首版设置合理限制：zip 大小、文件数量、单文件大小。具体数值可以配置，默认先保守。

Todo：

- [x] 新增 `ThemePackageService.InspectZipAsync`。
- [x] 实现 zip root 归一化。
- [x] 实现 Zip Slip 与非法路径检查。
- [x] 实现 manifest、runner、contractVersion、theme id 校验。
- [x] 实现包大小、文件数、可疑目录诊断。
- [x] 返回 `ThemePackageInspection`，包含 manifest、source root、warnings、blocking errors。

验收：

- [x] 合法 `fluid-static` zip 能被识别出 Theme id/name/version。
- [x] GitHub 下载式单顶层目录 zip 能被识别。
- [x] `../evil.txt`、绝对路径、非法 theme id 会被拒绝。
- [x] 缺失 `theme.json` 或 unsupported runner 会给出明确错误。
- [x] `process` runner Theme 会标记为“需要信任此 Theme 才能安装或激活”。

测试建议：

- ThemePackageService 单元测试用内存或临时文件生成 zip。
- 覆盖直接 root、单顶层目录、多个顶层目录、目录穿越、非法 id、坏 JSON。

### TD-06 Zip 安装、更新与回滚

目标：把通过校验的 Theme Package 安装到 DataRoot，并支持安全更新。

设计：

- 安装目标为 `<data>/themes/<theme-id>/`。
- 安装前先解压到 staging 目录，例如 `<data>/cache/theme-upload/<upload-id>/staging/`。
- 新安装：staging 通过校验后移动到目标目录。
- 更新已有 Theme：
  - 先把当前目录移动到 `<data>/themes/.backups/<theme-id>/<timestamp>/`。
  - 再把 staging 移动到 `<data>/themes/<theme-id>/`。
  - 任何失败都尝试回滚旧目录。
- Theme 配置 `<data>/state/theme-config/<theme-id>.json` 不随 Theme 文件更新而删除。
- 如果存在同 id Dev Link 且 Dev Link 当前启用，zip 更新仍写入 Installed Theme，但 active 解析会继续被 Dev Link shadow；Dashboard 必须提示。
- 不允许 zip 更新 `default-static` 覆盖内置参考实现。若需要基于默认 Theme 修改，应另起 id。

Todo：

- [x] 新增 `InstallOrUpdateAsync`，只接受已通过 inspection 的 package。
- [x] 实现 staging、backup、rollback。
- [x] 保留 Theme 配置与 Theme 私有 i18n 覆盖。
- [x] 阻止覆盖 `default-static`。
- [x] 安装完成后刷新 Theme catalog。
- [x] 安装操作写入审计日志或 build/admin log。

验收：

- [x] 新 Theme zip 安装后出现在 Theme 列表。
- [x] 同 id 更新后文件内容被替换，Theme 配置仍保留。
- [~] 更新失败时旧 Theme 仍可用。代码已实现回滚路径，仍缺专门的失败回滚测试。
- [x] `default-static` zip 覆盖被拒绝。
- [x] Dev Link shadow 状态不会被 zip 更新误导。

测试建议：

- 文件系统集成测试覆盖新安装、更新、失败回滚、配置保留。
- HomeServer 测试覆盖 active Theme 更新后 Preview 使用新文件。

### TD-07 Admin Dashboard 上传与 Theme Library

目标：让普通 Admin 在 Dashboard 里完成 Theme 上传、检查、安装、更新和激活。

设计：

- 新增 Theme Library 页面，建议路由 `/Admin/Site/Themes`。
- 现有 `/Admin/Site/Theme` 保持“当前 Theme 定制”职责；Theme Library 负责管理 Theme 来源。
- Theme Library 展示：
  - 当前 active Theme。
  - Installed Themes。
  - Dev Link Themes，仅在开发链接启用时展示。
  - 每个 Theme 的 id/name/version/source/runner/diagnostics。
- 上传流程：
  - Step 1：选择 zip。
  - Step 2：服务端 inspection，展示 manifest、warnings、blocking errors。
  - Step 3：确认安装或更新。
  - Step 4：安装成功后可选择设为当前 Theme。
- `process` runner Theme 必须展示“会在 Home Server 宿主中执行命令”的信任确认。

Todo：

- [x] 增加 Theme Library 页面和导航入口。
- [x] 增加 zip 上传 endpoint 或 Blazor form handler。
- [x] 展示 inspection 结果和 blocking errors。
- [x] 实现安装/更新确认动作。
- [x] 安装成功后提供“设为当前 Theme”动作。
- [x] 对 `process` runner 展示信任确认。

验收：

- [~] Admin 可以从浏览器上传合法 zip 并安装。页面和 handler 已实现，仍缺浏览器级上传 smoke 记录。
- [x] 上传坏包不会写入 `<data>/themes/`。
- [x] 更新已有 Theme 前能看到“更新”而不是误判为新安装。
- [x] 安装成功后不用手动输入 Theme id。
- [x] `process` Theme 没有确认信任时不能安装或激活。

测试建议：

- HomeServer 测试覆盖上传 endpoint、inspection result、安装成功、坏包拒绝。
- Dashboard render 测试覆盖 Theme Library 列表状态。
- Browser smoke 覆盖上传合法 zip、安装、设为当前 Theme、Preview。

### TD-08 安全边界与运行策略

目标：把 zip 上传和 process runner 的风险显式纳入产品边界。

设计：

- `fluid-static` Theme 是低风险模板 Theme，但仍需防路径穿越和非法文件。
- `process` runner Theme 本质上是“允许 Theme 在 Home Server 宿主内执行命令”，必须被标为受信任 Theme。
- 首版不做签名、远程 marketplace、container runner、沙箱隔离。
- Docker 镜像是否内置 Node.js 不由 Theme Package 决定。`process` Theme 的依赖缺失是 Theme runner 错误，而不是 Home Server 安装错误。
- 上传 zip 不自动执行 `installCommand`。
- Admin 删除 Theme 不在首版范围；先只做安装/更新，避免误删 active Theme 或配置。

Todo：

- [x] 在 inspection 结果中区分 warning 和 blocking error。
- [x] 对 `process` runner 增加 trust required 状态。
- [x] 上传和安装流程不执行 Theme 代码。
- [x] 构建阶段继续通过 ThemeRunner 统一处理 timeout、取消、非零退出码。
- [x] 文档写明 `process` runner 的部署依赖由宿主环境负责。

验收：

- [x] 上传 zip 阶段不会执行 Theme 包内命令。
- [x] `process` Theme 的风险在 UI 中可见。
- [x] 依赖缺失时 Build log 能指出启动命令失败。
- [x] 没有新增绕过 Theme Contract 直接写 `output/public/` 的路径。

测试建议：

- 构造 `process` runner zip，验证需要 trust confirmation。
- 构造恶意路径 zip，验证 inspection 拒绝且没有落盘到 Theme 目录。
- 构建测试覆盖 process runner 非零退出码仍映射为 Theme runner 错误。

### TD-09 文档、示例与验收套件

目标：让专项实现完成后，Theme 作者和 Admin 都能按文档完成闭环。

设计：

- 更新 `Themes/README.md`，加入 Theme 开发模式、Dev Link、Zip Package 约定。
- 新增或更新 `Themes/default-static/README.md`，说明如何复制默认 Theme 作为开发起点。
- 在 `Docs/Architecture.md` 中补充 ThemeResolver、Dev Link、Theme Package 的边界。
- 在 Dashboard 文案中避免临时占位，所有新增 UI 文本进入 i18n JSON。
- 最终验证必须覆盖源码 `dotnet run` 开发路径和 Docker 近似部署路径的契约差异。

Todo：

- [x] 更新 Theme 作者文档：源码运行、Dev Link、Preview、process runner 依赖。
- [x] 更新 Admin 文档：上传 zip、更新 Theme、设为当前 Theme、风险提示。
- [x] 更新架构文档：DataRoot 下 Theme 来源与 resolver 模型。
- [ ] 补齐测试清单并记录最终验证命令。
- [x] 若新增 UI，补齐 `zh-CN` 和 `en-US` Dashboard i18n。

验收：

- [~] 新开发者能按文档用外部 Theme repo 跑通 Preview。文档和代码路径已具备，尚缺最终 smoke 记录。
- [~] 普通 Admin 能按文档上传 zip 并设为当前 Theme。页面已具备，尚缺浏览器级上传 smoke 记录。
- [x] 文档明确 workspace 不承载 Theme 实现或 Theme 配置。
- [x] 文档明确 Docker 是可选验证方式，不是 Theme 开发的默认路径。

测试建议：

- 文档中的最小 Dev Link 示例在临时 DataRoot 下可运行。
- 文档中的 zip 包结构可被 ThemePackageService inspection 接受。
- `jq empty` 校验新增 Dashboard i18n JSON。

## 6. 实施顺序建议

1. TD-01：先抽 ThemeResolver，避免后续 Dev Link 和 Zip 逻辑散落到多个服务。
2. TD-02：落地 Dev Link 契约，先服务 Theme 作者开发循环。
3. TD-03：补开发者诊断和 Preview 错误可见性。
4. TD-04：把 active Theme 选择从文本框升级为 catalog picker。
5. TD-05：实现 zip inspection，先只检查不安装。
6. TD-06：实现安装、更新和回滚。
7. TD-07：接入 Admin Dashboard 上传流程。
8. TD-08：补齐 process runner 风险提示和安全约束。
9. TD-09：回写文档、示例和最终验证记录。

这个顺序的原则是：先统一 Theme 来源，再开放新来源；先能检查包，再允许安装；先能开发调试，再追求普通用户安装体验。

## 7. 总 Todo 清单

| 状态 | ID | 工作包 | 关键验收 |
| --- | --- | --- | --- |
| [x] | TD-01 | Theme Resolver 与 Catalog 抽象 | Generator 和 Dashboard 对同一 `themeId` 解析一致。 |
| [x] | TD-02 | Dev Link 契约 | 外部 Theme Root 可在源码 `dotnet run` 下直接 Preview。 |
| [~] | TD-03 | Theme 作者开发体验 | Dashboard 与 Build log 能显示 active Theme 的 source/root/runner；Preview 错误 UI 细分仍待补。 |
| [x] | TD-04 | Active Theme 选择与切换体验 | Admin 不再手填 Theme id，切换仍复用 Theme Migration。 |
| [x] | TD-05 | Zip Package 检查与校验 | 合法包可识别，坏包和路径穿越被拒绝。 |
| [~] | TD-06 | Zip 安装、更新与回滚 | 安装和更新写入 DataRoot，配置保留；失败回滚仍缺专门测试。 |
| [~] | TD-07 | Admin Dashboard 上传与 Theme Library | Admin 可上传、检查、安装、更新并设为当前 Theme；浏览器级上传 smoke 仍待补。 |
| [x] | TD-08 | 安全边界与运行策略 | `process` runner 需要显式信任，上传阶段不执行代码。 |
| [~] | TD-09 | 文档、示例与验收套件 | 主要文档已回写；最终验证记录、浏览器上传 smoke、Docker 近似验证仍待补。 |

## 8. 总体验收目标

开发者验收：

- [x] Theme 作者在本机用源码 `dotnet run` 启动 Home Server。
- [x] Theme 作者在外部 Theme repo 修改模板、CSS、manifest 或 schema。
- [x] Dev Link 指向外部 Theme repo 后，Dashboard 能识别该 Theme。
- [x] 设为 active Theme 后，Preview 刷新即可反映外部 Theme 文件变化。
- [~] 同一 Dev Link 契约在 Docker 模式下通过容器内挂载路径也能工作。契约已文档化，尚缺 Docker 近似验证记录。

用户验收：

- [~] Admin Dashboard 能上传一个合法 `fluid-static` Theme zip。页面与 Blazor form handler 已实现，尚缺浏览器级上传 smoke 记录。
- [x] Dashboard 能在安装前显示 Theme id、name、version、runner 和 warnings。
- [x] 安装后 Theme 出现在 Theme Library，并可设为当前 Theme。
- [x] 更新同 id Theme 后，Theme 文件更新但原 Theme 配置保留。
- [x] 上传坏包、路径穿越包、缺失 manifest 包不会污染 `<data>/themes/`。

构建与预览验收：

- [x] Full Build 使用 active Theme，输出仍由 Generator 收集到 `<data>/output/public/`。
- [x] Live Preview 使用一次性 input/output 目录，不污染 Full Build 输出。
- [x] Theme 不直接写 `<data>/output/public/`。
- [x] Theme source 文件变化进入构建指纹，避免短路掉 Theme 修改。

安全验收：

- [x] Zip 上传和 inspection 不执行 Theme 代码。
- [x] `process` runner Theme 必须显式确认信任。
- [x] Production 默认不启用 Dev Link。
- [x] `workspace` 内不出现 Theme 实现、Theme 配置、Theme 上传临时文件或构建产物。

## 9. 非目标

以下内容不进入本专项首版：

- 公开 Theme marketplace。
- Theme 签名、远程信任链和自动更新源。
- 独立 container runner 或沙箱 runner。
- Theme 删除与垃圾回收 UI。
- Theme dev server 反向代理和热模块替换。
- 自动执行 `installCommand`。
- 把 Docker 作为 Theme 作者开发的必需路径。

## 10. 最终验证建议

代码验证：

```bash
dotnet test Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj --no-restore --disable-build-servers -v:minimal /m:1 /nr:false
dotnet test Tests/Bocchi.HomeServer.Tests/Bocchi.HomeServer.Tests.csproj --no-restore --disable-build-servers -v:minimal /m:1 /nr:false
dotnet test Bocchi.slnx --no-restore --disable-build-servers -v:minimal /m:1 /nr:false
git diff --check
```

UI 与行为 smoke：

- 源码 `dotnet run`，临时 DataRoot，创建 Dev Link，Preview root。
- 修改外部 Theme CSS，刷新 Preview，确认变化生效。
- 上传合法 zip，安装，设为当前 Theme，Preview root。
- 上传恶意 zip，确认 Dashboard 显示拒绝原因且 `<data>/themes/` 未污染。
- 上传或安装 `process` Theme，确认信任提示和 build error 展示。
