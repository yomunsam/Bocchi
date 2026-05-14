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
| [~] | M2 | Content Workspace | 定义并实现内容目录、Markdown/frontmatter 解析和 SQLite 管理状态 | workspace 初始化、内容扫描、解析日志、`Docs/Milestones/M2/M2.md` | 能扫描文章、页面、作品、短文、友链和站点设置 |
| [ ] | M3 | Generator Pipeline | 生成标准化内容图、Theme 输入数据和本地静态输出 | 构建任务、`.bocchi/input/`、`output/public/` | Full Build 可产出完整本地站点目录 |
| [ ] | M4 | Home Server Dashboard | 提供可用的内网管理界面 | 后台首页、内容列表、编辑入口、构建日志 | 能通过 UI 管理内容并触发构建 |
| [ ] | M5 | Default SvelteKit Theme | 提供默认静态前端 | `Themes/default-svelte/`、Theme Contract 校验 | 首页、文章、页面、作品、短文、友链页面可静态输出 |
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

- `dotnet restore Bocchi.sln`、`dotnet build Bocchi.sln`、`dotnet test Bocchi.sln` 全部通过，0 警告 0 错误。
- `dotnet run --project Src/HomeServer/Bocchi.HomeServer` 启动到 `http://127.0.0.1:5081`，`/healthz` 返回 `Healthy`，`/` 返回包含 "Bocchi Home Server" 的页面。
- 集中包管理（Directory.Packages.props）与 `TreatWarningsAsErrors` 已生效。

## M2 Content Workspace

目标：让 Bocchi 能读懂本地内容，并把"长期可控、可一键带走的创作内容"和"Bocchi 自己的系统状态"做出干净切分。

详细规划与决策记录见 `Docs/Milestones/M2/M2.md`。

建议任务（细化项见 M2.md §5）：

- 定义 workspace 初始化流程，强制"内容空间 / Bocchi 系统空间"切分。
- 实现内容空间默认目录约定（年份目录作为一级分类、单篇 = 目录 + `assets/`、短文 = 单文件）。
- 实现 Markdown + YAML frontmatter 解析。
- 实现文章、页面、作品、短文、友链、站点设置的数据加载与校验（Photo 仅占位）。
- 实现 SQLite 管理状态（schema 迁移、文件 hash、扫描快照、错误聚合）。
- 实现内容校验和错误报告。
- 通过 LibGit2Sharp 把内容空间识别为 Git 工作区（本地能力：init/status/commit）。
- Home Server 后台增加最小"工作区状态"页面用于回路验证。
- 把 Serilog 文件 sink 切换到 `<workspace>/.bocchi/logs/`。

验收标准：

- 给定示例 workspace，可以扫描出所有 MVP 内容类型（Photo 除外）。
- 草稿、slug、分类、标签、发布时间和媒体引用能被识别。
- SQLite 能记录扫描状态、hash、错误和索引；不复制内容正文。
- 错误信息能指出具体文件和字段。
- 内容空间可独立打包带走，目录中无任何 Bocchi 系统痕迹与构建产物。

暂不做：

- 完整可视化编辑器（M4）。
- 标准化内容图、Theme 输入数据写入、媒体衍生品、增量构建（M3）。
- Git 远程接入（push/pull、GitHub、凭据）—— 与 M6 发布管线一起设计。
- 照片墙完整体验。

## M3 Generator Pipeline

目标：从内容工作区生成可供 Theme 消费的数据，并产出本地静态目录。

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

目标：让 Home Server 成为日常可用的个人内容工作台。

建议任务：

- 后台首页与状态总览。
- 内容列表、筛选和详情入口。
- Markdown 编辑或外部编辑器打开入口。
- 媒体引用查看。
- 站点设置编辑。
- Theme 配置 UI，根据 `config-schema.json` 自动生成表单。
- 构建和发布日志页面。

验收标准：

- 可以通过 UI 浏览和管理 MVP 内容类型。
- 可以修改站点设置和 Theme 配置。
- 可以触发构建并查看结果。
- 错误信息能定位到具体文件、字段或构建阶段。

暂不做：

- 多人权限系统。
- 公网 API。
- 富文本编辑器优先级不高，Markdown 优先。

## M5 Default SvelteKit Theme

目标：提供一套可真实使用的默认静态个人主页。

建议任务：

- 建立 `default-svelte` Theme。
- 实现 `theme.json`。
- 实现 `config-schema.json`。
- 实现 Theme 输入数据加载。
- 实现首页。
- 实现文章列表和详情页。
- 实现独立页面。
- 实现作品列表和详情页。
- 实现短文列表。
- 实现友链页。
- 实现基础响应式布局。

验收标准：

- Theme 可以被 Home Server 调用构建。
- 静态输出可直接打开或部署。
- 所有 MVP 内容类型都有对应展示。
- Theme 配置能影响实际页面。

暂不做：

- 评论系统。
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
- 内容空间是"源工程"：禁止出现派生产物（webp、缩略图、HTML、搜索索引）；衍生媒体目录固定为 `<workspace>/.bocchi/cache/derivatives/`，由 M3 实施。
- Serilog 文件 sink 切换到 `<workspace>/.bocchi/logs/bocchi-.log`。

详细决策与落地清单见 `Docs/Milestones/M2/M2.md`。

## 待决问题

- 默认搜索使用自研 JSON index 还是 Pagefind。
- Cloudflare Pages 发布优先手动目录流程还是自动化流程。
- 内容空间作为 Git 仓库时与远程（GitHub 等）的接入策略：与 M6 发布管线一起设计，包括凭据存储、push 触发时机、webhook 回路等。
