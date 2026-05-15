# Bocchi Milestone Plan

本文档是 Bocchi 的总体里程碑计划，也是后续排查和恢复上下文的入口。每个阶段推进时，都应先更新下方“总览 Todo List”，再进入具体任务；如果某个任务卡住，也优先通过该表定位所属模块、验收口径和下一步入口。

## 状态约定

- `[ ]` 未开始
- `[~]` 进行中
- `[x]` 已完成
- `[!]` 阻塞或需要决策

## 总览 Todo List

| 状态 | ID | 里程碑 | 目标 | 问题定位入口 | 完成标准 |
| --- | --- | --- | --- | --- | --- |
| [x] | M0 | 架构与计划基线 | 固化系统边界、Theme Contract 和总体路线 | `Docs/Architecture.md`、`Docs/Milestones.md` | 架构文档和里程碑文档存在，核心开放问题被列出 |
| [x] | M1 | Solution 骨架 | 建立 .NET Home Server、测试项目和基础目录 | solution 文件、`Src/HomeServer/`、`Tests/`、`Docs/Milestones/M1/M1.md` | 可启动空后台，可运行基础测试 |
| [x] | M2 | Content Workspace | 定义并实现内容目录、Markdown/frontmatter 解析和 SQLite 管理状态 | workspace 初始化、内容扫描、解析日志、`Docs/Milestones/M2/M2.md` | 能扫描文章、页面、作品、短文、友链和站点设置 |
| [x] | M3 | Generator Pipeline | 生成标准化内容图、Theme 输入数据和本地静态输出 | 构建任务、`.bocchi/input/`、`output/public/`、`Docs/Milestones/M3/M3.md` | Full Build 可产出完整本地站点目录 |
| [x] | M4 | Home Server Dashboard | 提供正式但亲和的个人发布后台、Setup、Identity、Markdown 编辑和受保护前台预览 | `Docs/Milestones/M4/M4.md`、`Docs/Milestones/M4/UI-Design.md`、`/Setup`、`/Admin`、`/` Preview | 第一个 Admin 可完成 Setup；Dashboard 可管理内容、设置、发布、构建详情和预览 |
| [~] | M5 | Default SvelteKit Theme | 提供默认静态前端 | `Docs/Milestones/M5/M5.md`、`Themes/default-svelte/`、Theme Contract 校验 | 首页、文章、页面、作品、短文、友链页面可静态输出 |
| [ ] | M6 | Feeds, Search and Publish | 完成 RSS、Sitemap、搜索索引和基础发布目标 | RSS/Sitemap/search index、Local/Cloudflare Pages 输出 | 本地发布可用，Cloudflare Pages 路径明确 |
| [ ] | M7 | Cloud Server 预留 | 为未来动态功能保留清晰接口 | Cloud Server ADR、动态功能候选列表 | 有边界设计，无无谓提前实现 |

## 推进规则

- 每完成一个小阶段，更新“总览 Todo List”的状态和必要说明。
- 如果阶段内新增决策，写入对应文档的“开放问题”或“决策记录”。
- 如果实现偏离 `Docs/Architecture.md`，先更新架构文档，再改代码。
- 每个里程碑结束时补一段验证记录，说明跑过的命令、手工检查和残留风险。
- 文档、代码和验证记录要一起推进，避免只能从聊天记录恢复项目状态。

## M0 架构与计划基线

目标：把 README 中的构想转成可执行的系统边界和里程碑索引。

交付物：

- `Docs/Architecture.md`
- `Docs/Milestones.md`
- 核心开放问题清单

验收标准：

- 三段式架构明确：Home Server、Page Frontend、Cloud Server。
- Theme Contract v1 有初始定义。
- 内容工作区、SQLite 职责和构建流水线有明确方向。
- 总览 Todo List 能作为后续恢复上下文和定位问题的入口。

当前状态：已完成。

验证记录：

- 已新增 `Docs/Architecture.md`。
- 已新增 `Docs/Milestones.md`。
- 已执行 `git diff --check`，未发现空白错误。

## M1 Solution 骨架

目标：建立可以持续演进的工程结构。

建议任务：

- 创建 .NET solution。
- 创建 Home Server 项目。
- 创建核心库项目，例如内容模型、workspace、generator contract。
- 创建测试项目。
- 建立基础配置、日志和本地开发启动方式。
- 预留 `Themes/` 或 `Src/Themes/` 目录策略。

验收标准：

- `dotnet build` 通过。
- Home Server 可以本地启动。
- 有最小健康页或后台壳。
- 有最小单元测试或集成测试。

暂不做：

- 完整 UI。
- 真实 Theme 构建。
- Cloud Server。

当前状态：已完成。详细规划与验证记录见 `Docs/Milestones/M1/M1.md`。

验证记录：

- `dotnet restore Bocchi.slnx`、`dotnet build Bocchi.slnx`、`dotnet test Bocchi.slnx` 全部通过，0 警告 0 错误。
- `dotnet run --project Src/HomeServer/Bocchi.HomeServer` 启动到 `http://127.0.0.1:5081`，`/healthz` 返回 `Healthy`，`/` 返回包含 "Bocchi Home Server" 的页面。
- 集中包管理（Directory.Packages.props）与 `TreatWarningsAsErrors` 已生效。

## M2 Content Workspace

目标：让 Bocchi 能读懂本地内容，并把"长期可控、可一键带走的创作内容"和"Bocchi 自己的系统状态"做出干净切分。

详细规划与决策记录见 `Docs/Milestones/M2/M2.md`；详细验证记录见 `Docs/Milestones/M2/M2.md` §8。

完工内容：

- workspace 初始化流程落地，强制"内容空间 / Bocchi 系统空间"切分（`WorkspaceLayout` + `ContentSpaceLayout`）。
- 内容空间默认目录约定生效：年份目录一级分类；Post / Work = 目录 + `assets/`；Page 不按年份；Note = 单文件 Markdown；Friends / Site = YAML。
- Markdown + YAML frontmatter 解析（Markdig + YamlDotNet）：六个内容类型独立 Loader，统一错误模型 `ContentValidationError(Severity / Code / Field / Message)`。
- SQLite 管理状态（`Microsoft.Data.Sqlite`）：`SchemaMigrator` 基于 `PRAGMA user_version`；`ContentStateStore` 持久化文件 hash、内容索引、扫描运行、错误聚合；不复制正文。
- `ContentScanner` 端到端打通：年份目录校验、媒体引用校验、孤儿媒体 Info、可疑派生产物 Warning。
- 内容空间 Git 集成（LibGit2Sharp）：本地 `init/status/commitAll`；远程接入按决策延后到 M6。
- Home Server `/workspace` 页面、首页入口；Serilog 文件 sink 切换到 `<workspace>/.bocchi/logs/`。

验收：`dotnet build` / `dotnet test` 全绿；`dotnet run` 启动后 `/workspace` 可显示工作区根、Git 状态、扫描结果与错误列表；`<workspace>/content/` 目录可独立打包，不含任何 Bocchi 系统痕迹。

暂不做（已显式延后）：

- 完整可视化编辑器（M4）。
- 标准化内容图、Theme 输入数据写入、媒体衍生品、增量构建（M3）。
- Git 远程接入（push/pull、GitHub、凭据）—— 与 M6 发布管线一起设计。
- 照片墙完整体验。

## M3 Generator Pipeline

目标：从内容工作区生成可供 Theme 消费的数据，并产出本地静态目录。

详细规划：见 [`Docs/Milestones/M3/M3.md`](./Milestones/M3/M3.md)。

建议任务：

- 实现标准化内容图。
- 实现 Theme 输入数据写入。
- 实现 Full Build。
- 实现媒体复制。
- 实现构建 manifest。
- 实现输出目录校验。
- 提供 CLI 或后台按钮触发构建。

验收标准：

- `.bocchi/input/` 中存在稳定 JSON 输入。
- `output/public/` 中存在可部署静态产物。
- 构建日志可追踪每个阶段。
- 失败时能定位到内容错误、Theme 错误或输出错误。

暂不做：

- 高级增量构建。
- 多发布目标并发。
- 复杂构建队列。

## M4 Home Server Dashboard

目标：让 Home Server 成为正式、日常可用、视觉亲和的个人内容发布后台，而不是临时后台壳或硬核专业工作台。

详细规划：见 [`Docs/Milestones/M4/M4.md`](./Milestones/M4/M4.md)。

当前状态：已完成。M4 已落地 EF Core + Identity、首次 Setup、第一个 Admin、本地登录、GitHub / OIDC 配置、`/Admin` Dashboard、内容扫描与编辑、设置、用户管理、发布面板、受保护 `/` Preview Host 与浮动工具栏。验证记录见 `Docs/Milestones/M4/M4.md` §11。

关键方向：

- Home Server Dashboard 固定在 `/Admin`，前台实时预览固定在 `/`。
- 首次启动进入 `/Setup`，初始化 EF Core SQLite 数据库并创建第一个 Admin。
- M4 引入 ASP.NET Core Identity、EF Core、SQLite、单一 `Admin` 角色和默认全站鉴权。
- 底层支持多用户，但不是多租户；除 `Admin` 之外的用户没有 Dashboard 权限。
- Dashboard 要像柔和的个人发布 App：低到中信息密度、App-like 内容列表、普通用户可理解的发布状态；先集中确认 UI 风格、信息架构、Light / Dark / Auto 和移动端方案，再开发主要功能。
- 内容编辑以 Markdown 分栏预览为主，不做 WYSIWYG。
- GitHub 与通用 OpenID Connect Provider 通过设置页启用，Logto 只作为 OIDC 示例，不写死到模型中。
- Home Server 内的前台站点是受保护预览模式，可由 Preview Host 注入浮动工具栏，提供返回 Dashboard 和内容编辑入口。

建议任务：

- UI 风格与设计基线：柔和粉蓝、文字 Logo、Dashboard 外观下拉、低密度 App-like 内容列表、组件状态、响应式布局和外观 token。
- EF Core + ASP.NET Core Identity：`BocchiDbContext`、SQLite migrations、Admin role、用户禁用状态。
- Welcome / Setup：初始化数据库、创建第一个 Admin、关闭公开注册。
- 授权路由：`/Admin` Dashboard、`/` 受保护 Preview、默认 fallback auth policy。
- 第三方登录设置：GitHub OAuth 与通用 OpenID Connect Provider。
- Dashboard Shell：一级/二级导航，桌面二级菜单，移动端二级分类下拉。
- 内容列表、筛选、详情入口和 Markdown 分栏编辑器。
- 站点设置、Theme 配置 UI、数据库状态、用户管理。
- 面向普通用户的发布/检查状态，以及高级构建日志、manifest、artifact 树和 zip 下载页面。
- 前台预览浮动工具栏和 route → content 编辑跳转。

验收标准：

- 首次启动可完成 Setup，第一个账户自动成为 Admin，公开注册随即关闭。
- 未登录不能访问 `/Admin` 或 `/`；非 Admin 用户没有 Dashboard 权限。
- Dashboard 在桌面和移动端都可用，Light / Dark / Auto 生效。
- 可以通过 UI 浏览和管理 MVP 内容类型，至少 Post / Page 支持 Markdown 分栏编辑与保存。
- 可以修改站点设置、Theme 配置和第三方登录设置。
- 可以触发构建、查看普通用户可理解的发布/检查状态、进入高级日志、查看产物并定位失败阶段。
- `/` 可打开受保护前台预览，Preview Toolbar 可返回 Dashboard，并在可定位内容页跳转到编辑页面。

暂不做：

- 多租户。
- 复杂多角色权限系统。
- 公网 API。
- WYSIWYG 编辑器。
- 默认 SvelteKit Theme 的完整视觉实现（M5）。

## M5 Default SvelteKit Theme

目标：提供一套可真实使用的默认静态个人主页。

详细规划：见 [`Docs/Milestones/M5/M5.md`](./Milestones/M5/M5.md)。

当前状态：设计阶段已开工。M5 先补齐默认 Theme 物化、`theme-config.json`、Theme 输出登记和 manifest 对账，再实现 `default-svelte` 的 SvelteKit 静态页面。默认视觉方向为克制现代的前台个人主页：排版优先、网格清晰、纸墨中性色、少量焦橙 accent，不把 Dashboard 视觉或外部静态原型代码混入 Theme 架构。

建议任务：

- 建立 `Themes/default-svelte/` 作为仓库内置默认 Theme 源。
- 明确仓库内置 Theme 到工作区 `<workspace>/themes/default-svelte/` 的物化方式。
- 补齐 `theme-config.json` 输入，让 Dashboard Theme 设置参与构建。
- 新增 Theme 输出收集阶段，把 SvelteKit 输出登记为 `ArtifactKind.ThemeOutput` 并纳入 manifest。
- 实现 `theme.json`、`config-schema.json` 和 Theme 输入数据加载。
- 实现首页、文章列表和详情页、独立页面、作品列表和详情页、短文列表、友链页、404 页面。
- 实现基础响应式布局、Light / Dark、focus-visible 和移动端导航。

验收标准：

- Theme 可以被 Home Server 调用构建。
- 静态输出可直接打开或部署。
- 所有 MVP 内容类型都有对应展示。
- Theme 配置能影响实际页面。
- `.bocchi-manifest.json` 包含 Theme HTML / CSS / JS / assets，不存在未登记输出。

暂不做：

- 评论系统。
- 搜索 UI 和搜索索引。
- 多语言内容模型。
- 复杂动画。
- 多套视觉主题。

## M6 Feeds, Search and Publish

目标：让站点具备基本发布能力和可发现性。

建议任务：

- RSS 生成。
- Sitemap 生成。
- 静态搜索索引生成。
- Local Directory 发布目标。
- Cloudflare Pages 发布路径。
- 发布历史记录。
- 构建产物 manifest 与发布 manifest 对齐。

验收标准：

- RSS 和 Sitemap 可被验证。
- 搜索能覆盖文章、页面、作品和短文。
- 本地目录发布可重复执行。
- Cloudflare Pages 的产物目录和操作流程明确。

暂不做：

- 动态搜索服务。
- 复杂 CI/CD。
- 多账号发布凭据管理。

## M7 Cloud Server 预留

目标：为动态功能留出边界，但不提前制造复杂度。

候选功能：

- 评论。
- 动态短文同步。
- Webmention。
- 轻量访问统计。
- 订阅回调。
- 需要鉴权的动态 API。

验收标准：

- 明确哪些能力必须进入 Cloud Server。
- 明确 Cloud Server 与静态站点、Home Server 的数据边界。
- 没有在 MVP 中提前实现尚未确定的动态服务。

## 决策记录

### 2026-05-13

- Bocchi 采用"三段式"架构：Home Server、Page Frontend、Cloud Server。
- Home Server 是内网 CMS 和构建器，不作为公网 API Server。
- Page Frontend 通过 Theme Contract 接入，默认 Theme 暂定 SvelteKit。
- 内容事实来源优先放在 Markdown 和媒体文件中，SQLite 只承担管理状态、索引和缓存职责。
- Cloud Server 只预留，等评论、动态搜索、统计等真实需求出现后再实现。

### 2026-05-13 (M1)

- Home Server 后台 UI 采用 **Blazor Web App + InteractiveServer 渲染模式**。理由：后台是内网交互工作台，需要表单、按 schema 生成配置 UI、构建/发布日志的实时反馈；Blazor Server 与 .NET 生态一致，避免另外维护 SPA 构建链；将来若需要 SSR + 客户端混合，也可平滑切换到 InteractiveAuto。
- 日志：采用 **Serilog**（Console + File sink），通过 `appsettings.json` 配置；M1 文件输出落在运行目录 `logs/`，M2 引入 workspace 概念后切换到 `.bocchi/logs/`，期间无过渡方案，仅修改配置即可。
- 包管理：仓库根启用 **Central Package Management**（`Directory.Packages.props`）；所有项目版本统一在该文件维护。
- 公共编译选项：`Nullable=enable`、`ImplicitUsings=enable`、`TreatWarningsAsErrors=true`、`LangVersion=latest`、`AnalysisLevel=latest-recommended` —— 一开始就把质量门槛拉满。
- 测试：xUnit + FluentAssertions；Home Server 集成测试基于 `Microsoft.AspNetCore.Mvc.Testing`。
- 监听策略：Home Server 默认仅绑定 `http://127.0.0.1:5081`，与 Architecture §12 一致。

### 2026-05-14 (M2)

- 内容工作区在物理上严格切分为 **内容空间** 与 **Bocchi 系统空间**。内容空间默认位于 `<workspace>/content/`，包含纯创作资产（Blog、独立页面、作品集、短文、友链、站点设置），可独立打包/迁移、可作为独立 Git 仓库；Bocchi 系统空间位于 `<workspace>/{themes,.bocchi,output}/`，与 Bocchi 项目同寿。理由：让用户的内容资产独立于"Bocchi 这个程序"的命运，未来 Bocchi 被遗弃/重构时可以一键带走。
- 内容空间内的 Post / Work / Note / Photo 强制使用 **年份目录** 作为一级分类（`<kind>/<year>/...`，年份正则 `^\d{4}$`）。Pages 不按年份分类。
- Post / Work 单篇为 "目录形式"：`<kind>/<year>/<slug>/index.md` + `assets/`，`assets/` 仅放该篇的原始媒体；frontmatter 中以相对路径引用。
- Note 采用 **Markdown 单文件**（`notes/<year>/<filename>.md`），一条短文一个文件；正文即 Markdown 正文，不在 frontmatter 中重复 `text` 字段。关闭原"短文存储格式"待决问题。
- frontmatter 一律使用 **YAML**。关闭原 frontmatter 格式待决问题。
- Markdown 引擎 `Markdig`，YAML 引擎 `YamlDotNet`。
- SQLite 客户端使用 `Microsoft.Data.Sqlite`，schema 版本由 `PRAGMA user_version` 显式管理；不引入 EF Core；SQLite 只承担状态/索引/缓存职责，**绝不复制内容正文**。
- 内容空间作为 Git 工作区使用 `LibGit2Sharp`：M2 提供 `init / status / commit` 等本地能力；远程接入（push/pull、GitHub、凭据存储）显式延后到 M6 发布管线。
- 2026-05-14 复查：`LibGit2Sharp` 0.31.0 是当前稳定版本，NuGet 计算兼容 `net10.0`；Bocchi 当前只使用内容空间本地 init/status/commitAll，继续使用该库。
- 内容空间是"源工程"：禁止出现派生产物（webp、缩略图、HTML、搜索索引）；衍生媒体目录固定为 `<workspace>/.bocchi/cache/derivatives/`，由 M3 实施。
- Serilog 文件 sink 切换到 `<workspace>/.bocchi/logs/bocchi-.log`。

详细决策与落地清单见 `Docs/Milestones/M2/M2.md`。

### 2026-05-14 (M4 planning)

- M4 的 Home Server Dashboard 升级为正式 CMS / Blog 后台方向：先确认 UI 风格和信息架构，再进入主要页面开发。
- Home Server 应使用正式 ASP.NET Core Identity 用户系统；底层多用户但非多租户，只设置一个 `Admin` 业务角色，第一个账户默认 Admin，Setup 完成后关闭公开注册。
- M4 起 Home Server 应用状态采用 EF Core + SQLite 作为正式数据访问方式；内容事实仍在 Markdown / YAML / 原始媒体文件中，EF Core 不保存正文事实。
- Dashboard 基础 URL 为 `/Admin`；Home Server 内前台站点基础 URL 为 `/`，且作为登录后的实时预览模式存在。
- 前台预览模式可以由 Home Server Preview Host 注入浮动工具栏，用于提示 Preview 状态、返回 Dashboard，并在文章 / 页面 / 作品详情页跳转到后台编辑。
- 第三方登录包含 GitHub 和通用 OpenID Connect Provider；Logto 只作为 OIDC 示例，不写死到类型名、表名或路由中。

### 2026-05-14 (M4 UI baseline)

- M4-T01 的 UI 方向已确认并写入 `Docs/Milestones/M4/UI-Design.md`：Bocchi Admin 走柔和、低到中信息密度、App-like 的个人发布工具方向，不走 GitHub 风格仓库面板、企业 CMS 或硬核运维工作台方向。
- 左上角 Logo 暂定只显示文字 `Bocchi`；右上角 Dashboard 外观 / dark mode 切换使用紧凑下拉，不使用占宽分段控件；前台业务 Theme 选择另放正文设置组件。
- 主内容区优先使用移动端友好的简单列表 / feed row，避免宽密表格和底部堆叠小框框。
- Publish / Build 在普通入口中表达为人能理解的发布/检查状态；raw log、manifest、artifact tree 进入高级详情。
- 视觉可参考 `Docs/Milestones/M4/Assets/m4-ui-style-direction-2026-05-14.png`；后续 UI 设计、效果图、实现和评审使用 `.codex/skills/bocchi-ui-style/SKILL.md` 复位风格护栏。
- Bocchi 的 anime-inspired 灵感只允许进入抽象气质、低饱和粉蓝和个人创作氛围；不得复制任何可识别角色、服装、发型、姿势、乐队标识或具体场景。
- 首批 UI 代码基线已进入 M4-T06：`MainLayout`、全局外观 token、Dashboard 外观下拉、`BocchiStatusPill`、`BocchiListRow`，以及现有 Home / Workspace / Build 页面低密度重排。该基线不改变前台业务 Theme Contract，构建页中的 `Theme id` 明确表示前台业务 Theme。

## 待决问题

- 内容空间作为 Git 仓库时与远程（GitHub 等）的接入策略：与 M6 发布管线一起设计，包括凭据存储、push 触发时机、webhook 回路等。
