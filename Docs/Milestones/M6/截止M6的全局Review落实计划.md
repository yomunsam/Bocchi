# 截止 M6 的全局 Review 落实计划

> 日期：2026-05-27
> 来源：`Docs/Milestones/M6/截止M6的全局Review报告.md`
> 目的：把全局 Review 中确实存在、值得进入后续工作的事项转成可派发计划；同时记录需要讨论或不同意的判断，避免把宽泛审查结论直接变成无边界重构。

## 0. 总结判断

这份 Review 的整体扫描质量较好：它抓到了 M6 后最应该关注的三类问题：安全边界、大文件可维护性、重复代码。报告对 Bocchi 现有架构优点的概括也基本准确，特别是 DataRoot / workspace、ContentModel、Theme Contract 和 i18n 三层分离。

但报告的优先级需要过滤。它把一些“真实存在但暂时可接受”的设计债务提升得偏高，也把部分内部诊断文案、Theme 元数据文案和用户可见 UI 文案混在一起评价。后续不能按报告原顺序全量开工，应按风险、边界清晰度和可验证性切片。

M6 已完成的结论不需要回退。本计划不重新打开 M6 localization 主线；它作为进入 M7 前后的工程治理队列。

## 1. 需要落实的事项

| ID | 优先级 | 事项 | 复核结论 | 推荐处理方式 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| R0 | P0 | 收齐当前 Admin 清理改动 | 当前 Admin 目录移动、`Build.razor` 拆分和 `.gitignore` 例外已经进入 staged 状态；这是进入下一批清理前必须先固化的基线。 | 单独提交这一批结构整理，不混入后续 ThemeCustomization / 安全改动。 | `git status --short` 中这批变更没有 unstaged / untracked 漏项；`dotnet test Tests/Bocchi.HomeServer.Tests/Bocchi.HomeServer.Tests.csproj --no-restore --disable-build-servers -v:minimal /m:1 /nr:false` 和 `git diff --check` 通过。 |
| R1 | P0 | 登录与 Setup 安全边界复核 | `AccountEndpoints` 中多处 POST 使用 `DisableAntiforgery()`，登录使用 `lockoutOnFailure: false`，外部登录会按 email 自动绑定已有用户，Setup 密码短期放入 Data Protection Cookie。它们不一定都是已确认漏洞，但都是进入 M7 发布能力前应定性的安全边界。 | 先做安全设计小文档或 review prompt，再实施最小闭环：明确 antiforgery / SameSite / Origin 检查策略；登录失败延迟或锁定策略；外部登录改为已登录 Admin 显式绑定或 Setup 阶段授权绑定；Setup pending admin 优先改服务端短期状态。 | 有文档化威胁模型；关键路径有 HomeServer 测试；外部登录不能仅凭 email 首次自动登录；登录失败策略可验证；Setup Cookie 不再携带密码明文 JSON 的受保护 payload，或保留时有明确风险接受说明。 |
| R2 | P1 | Admin 大文件继续拆分 | `Build.razor` 已从 1403 行拆到约 492 行；`ThemeCustomization.razor` 仍约 1158 行，`Settings/Index.razor` 也曾超过 1000 行阈值。 | 先拆 `Admin/Site/ThemeCustomization.razor`，只做 C# partial 拆分，不改 UI、CSS class、路由和 i18n key。`Settings/Index.razor` 等本轮提交后再评估。 | 单文件降到 900 行以内；路由不变；相关 HomeServer 测试通过；无用户可见行为变化。 |
| R3 | P1 | `DefaultStaticTemplateRenderer.cs` 拆分 | 1540 行属实，职责混合了输入读取、模型映射、页面渲染、URL 相对化和资产复制。它是 default-static 后续维护风险最高的文件。 | 在补足渲染器窄测试后拆分，优先提取输入读取 / 模型映射 / URL 相对化 / 写文件辅助；不要改变 Theme input contract，也不要让 Theme 重新推导 SEO / URL 事实。 | Generator tests 通过；新增 focused tests 覆盖相对 URL、语言切换、SEO head、资产复制；拆分前后生成的代表性 HTML 等价。 |
| R4 | P1 | `ThemeSettingsService.cs` 拆分 | 1113 行属实，同时处理 Theme catalog、schema 解析、配置保存、i18n override 和 Page Contract 展示模型。 | 在 ThemeCustomization 拆分后处理，避免 UI 和 service 两个巨大 diff 叠在一起。建议按真实职责拆：schema 读取/字段归一、配置值存取、Theme i18n override、Dashboard view model 组装。 | 现有 Theme customization / Theme library 测试通过；无配置文件格式变化；Dashboard 仍能加载、保存、回退 active Theme 设置。 |
| R5 | P2 | SHA-256 与 Category 展开重复 | 多处 `SHA256.HashData` + hex 编码重复，且存在 `HashUtil` / `Sha256Util` 两个相近工具；`FlattenPostCategories` 在多个 Generator 位置重复。 | 做小型工具收敛，但不要上升为跨项目“大平台”。Hash helper 可放在 Generator 内部合适命名空间；Category flatten 优先贴近 `GraphPostCategory` 所在层。 | 删除重复实现；调用点可读；相关 Generator tests 通过；`git diff --check` 通过。 |
| R6 | P2 | default-static focused tests | `DefaultStaticTemplateRenderer` 目前主要靠端到端测试间接覆盖；`DefaultStaticInlineTextRenderer` 和 `DefaultStaticThemeText` 的边界测试不足。 | 在拆 renderer 前后补单元/窄集成测试，优先覆盖 `[color=...]`、Theme 文案 fallback、语言回退和 HTML escape 边界。 | 新测试能独立定位 renderer/text 退化；不只依赖完整 pipeline 输出。 |
| R7 | P2 | UI 文案与诊断文案分类 | `blazor-error-ui` 可见英文属实；`EditorDraftService` 默认 frontmatter 和 CommonDisplayDefaults 也确实写在代码中。但 Services 层异常、Theme schema 文案不应一概按 Dashboard UI i18n 处理。 | 先分类：用户可见 UI 文案必须进 Dashboard i18n；用户内容模板默认值应走内容模板策略；Theme schema 是 Theme 元数据，可后续设计 localized schema；内部异常优先用稳定 code + 边界处本地化，不强行全部变资源 key。 | 形成分类清单；优先消除明显 UI 硬编码；不为了“零硬编码”牺牲诊断可读性。 |
| R8 | P3 | Generator 对 default-static 的编译期依赖 | `Bocchi.Generator.csproj` 直接引用 `Bocchi.Theme.DefaultStatic` 属实，`ThemeResolver` / `ThemeRunner` 也直接调用 built-in default-static。 | 这是边界味道，但目前也是内置 reference Theme 和 `fluid-static` runner 的现实实现。建议等 M7 publish/search 稳定后做 ADR：是保持 built-in plugin，还是把 renderer 注册变成可插拔服务。 | 有清晰 ADR；没有在缺少真实第二个内置 runner 前写空抽象；GeneratorContract 不被 default-static 反向污染。 |
| R9 | P3 | BuildSession `_bag` 与 Pipeline 注入数量 | 弱类型 bag 和 15 个构造依赖属实，但当前 pipeline 顺序显式、阶段数量有限，尚未造成实际回归。 | 记录为观察项。只有在新增 stage 或出现 key/type 错误时，再考虑 typed session items 或 stage collection 排序。 | 不作为 M7 前置项；若改动，必须保持 stage 顺序显式可审查。 |
| R10 | P3 | LibGit2Sharp 同步 I/O | 同步 Git 操作存在阻塞风险，但风险取决于是否在请求线程直接执行、仓库规模和调用频率。 | M7 发布相关工作中顺手复核调用路径；如位于用户请求链路，优先改为后台任务或明确进度模型。 | 有调用路径结论；必要时有任务化执行与取消/超时策略。 |
| R11 | P3 | MainLayout / Login 样式整理 | `MainLayout.razor` 427 行未到拆分阈值，但可读性一般；`Login.razor` 使用 Tailwind utility class，和多数 Admin 页面风格不完全一致。 | 低优先级 UI 整理。仅在做 Auth/Admin shell UI 时处理，不单独抢占 M7 前置时间。 | 不引入新硬编码文案；视觉回归用浏览器 smoke 或截图确认。 |

## 2. 不完全同意或需要讨论的判断

1. “4 个文件超过 1000 行阈值”的表述不严谨。当前真正超过 1000 行的是 `DefaultStaticTemplateRenderer.cs`、`ThemeSettingsService.cs`、`ThemeCustomization.razor` 等；`ThemeInputWriter.cs` 924 行、`ContentGraphBuilder.cs` 745 行是复杂文件，但不是 AGENTS.md 意义上的硬阈值问题。
2. “Services 层硬编码中文异常消息违反 UI i18n”需要拆开看。UI 文案必须 i18n；异常和诊断更应该稳定、可搜索、可脱敏，并在展示边界转成本地化信息。直接把所有 exception message 资源化，可能降低调试质量。
3. `config-schema.json` 的 `title` / `description` 是 Theme 元数据，不等同 Dashboard UI 文案。它们会被 Dashboard 展示，所以长期应支持 localized schema 或多语言 display metadata；但这不是简单把 schema 文案搬进 Dashboard JSON 就能解决的问题。
4. Loader 相似结构不必急着抽基类。Post/Page/Work/Note 的 frontmatter 语义不同，抽象过早容易把差异藏起来。更好的方向是提取少量语义明确的 YAML/value parser helper。
5. `GeneratorPipeline` 构造函数依赖多不是即时坏味道。显式注入让 stage 顺序容易审查；`IEnumerable<IBuildStage>` 只有在阶段注册和排序规则成熟后才更优雅。
6. `BuildSession._bag` 是潜在运行时风险，但当前有 `BuildSessionKeys` 和测试覆盖，不应在没有实际痛点时单独重构。
7. Generator 依赖 default-static 是真实边界问题，但不是马上移除的高优先级任务。内置 reference Theme 与外部 Theme 生态的关系需要 ADR，而不是先写一层空 DI。
8. CSRF 报告应表述为“需要威胁模型复核”，不是直接定性为漏洞。当前 cookie 的 SameSite、是否跨站可触发、端点是否只被同源表单使用，都需要一起判断。
9. 登录失败锁定/延迟是应该做的安全卫生项，但 Bocchi 的默认部署前提仍是内网个人 Home Server；它应该进入 M7 发布能力前的安全门，不必阻塞当前 Admin 结构清理提交。

## 3. 推荐执行顺序

### S0：固化当前 Admin 结构整理

目标：把已经完成的目录移动、`.gitignore` 例外和 `Build.razor` partial 拆分作为干净基线。

验收：

- staged snapshot 完整。
- HomeServer tests 通过。
- `git diff --check` 通过。
- 不混入本计划后续事项。

### S1：安全边界 review 与最小修复

目标：在 M7 publish/search/feed 继续推进前，先把 Auth / Setup / External Login 的安全边界说清楚并修掉最容易踩坑的点。

验收：

- 有威胁模型和取舍记录。
- 有覆盖登录失败策略、外部登录绑定、Setup pending admin 的测试。
- `DisableAntiforgery()` 的保留或移除都有逐端点理由。

### S2：Admin 可维护性第二刀

目标：拆 `Admin/Site/ThemeCustomization.razor`，必要时随后处理 `Settings/Index.razor`。

验收：

- 只做结构拆分，不改产品行为。
- 单页文件低于 900 行。
- HomeServer tests 通过。

### S3：default-static renderer 收束

目标：在不改 Theme Contract 的前提下，给默认 Theme renderer 建立更清晰的内部边界和 focused tests。

验收：

- `DefaultStaticTemplateRenderer.cs` 拆分后职责清晰。
- SEO / URL / localization 仍来自 Generator 和 Theme input 的明确事实，不回退到 Theme 自行推导。
- Generator tests 通过。

### S4：ThemeSettingsService 收束

目标：把 Theme settings 的 schema、配置存取、i18n override、Dashboard view model 组装拆开。

验收：

- Theme customization 行为不变。
- 现有配置兼容。
- Dashboard JSON 资源仍 valid。

### S5：重复代码和测试补强

目标：小步清理 Hash、Category flatten、ContentModel slug 边界、default-static text/inline renderer 测试。

验收：

- 每个小改动都有直接测试或既有 focused test 覆盖。
- 不把简单 helper 扩成跨项目框架。

## 4. 不阻塞 M7 的范围

以下事项可以进入 backlog，但不应在 M7 前强行解决：

- 完全移除 Generator 对 default-static 的编译期引用。
- 重写 GeneratorPipeline stage 注册模型。
- Typed BuildSession item 大改。
- Loader 基类化。
- MainLayout / Login 的纯样式统一。
- `config-schema.json` 多语言 schema 设计。

这些事项都是真实技术债或设计议题，但目前缺少足够强的回归风险或真实第二实现压力。过早动手容易把 M7 之前的工程治理变成大面积架构重写。

## 5. 后续派发模板要点

给执行 Session 的 prompt 应至少包含：

- 先读 `AGENTS.md`、本计划、原 Review 报告和相关代码文件。
- 明确本轮只做一个 slice。
- 不读取 `**/Vibe/`。
- 不改无关 UI 文案；新增用户可见文案必须进 Dashboard i18n。
- 大文件拆分优先 partial / helper，保持路由、CSS class、i18n key 和可见行为不变。
- 默认验证命令使用串行、禁用 build server 的 `dotnet test`，并跑 `git diff --check`；涉及 JSON 时跑 `jq empty`。

## 6. R1 安全边界落实记录（2026-05-27）

威胁模型：

- 攻击者能力：能诱导 Admin 浏览恶意站点，向本机 Home Server 发起跨站表单 POST；也可能知道 Admin 用户名并反复尝试密码；还可能控制一个与 Admin email 相同的外部身份账号。
- 保护目标：Setup 首个 Admin 密码、本地登录入口、外部登录绑定关系，以及账户相关端点的同源操作边界。
- 部署前提：Bocchi Home Server 仍以本机/内网个人工具为默认，不假设公网多租户；但进入 M7 发布能力前，认证和 Setup 的默认边界不能依赖“用户不会点恶意页面”。

本轮最小修复：

- 对 `AccountEndpoints` 中仍禁用 antiforgery token 的 POST 增加同源守卫：若浏览器发送 `Sec-Fetch-Site: cross-site`，或 `Origin` / `Referer` 与当前 Host 不同源，直接返回 `400 Bad Request`。
- 暂不把这些 SSR 表单整体切到 antiforgery token：当前 Setup gate、匿名登录页和测试客户端仍依赖无 token 的普通 form post；强行迁移会把本轮从安全边界修复扩大成表单渲染和测试基础设施改造。保留的风险是缺少浏览器来源头的客户端仍可提交；接受理由是现代浏览器跨站表单会带来源元数据，且相关 Cookie 使用 `SameSite=Lax`。
- 登录改为 `lockoutOnFailure: true`，并显式配置 5 次失败、5 分钟 lockout；旧用户登录时若未启用 lockout，会先开启。
- 外部登录回调不再按 email 自动绑定已有用户。只有已经存在的 Identity external login 绑定可以登录；显式绑定 UI 仍是后续小设计，不在本轮补。
- Setup 两步之间不再把 pending Admin JSON 放入 Cookie。服务端使用短期 `IMemoryCache` 保存 `PendingSetupAdmin`，Cookie 只携带 Data Protection 限时保护的随机句柄。服务重启或 20 分钟过期后，用户需要回到 Setup 第一步重填。

验证覆盖：

- `AccountPost_RejectsCrossSiteOrigin` 覆盖 Setup / Login / External Login / UI language POST 的跨站来源拒绝。
- `LoginPost_LocksOutAfterRepeatedFailures` 覆盖失败登录进入 Identity lockout。
- `ExternalLoginCallback_DoesNotAutoBindByMatchingEmail` 覆盖同 email 外部账号不会自动绑定或登录。
- `SetupPendingAdminCookie_StoresOnlyServerSideHandle` 覆盖 Cookie payload 不再包含 pending Admin 密码 JSON。
