# 截止 M6 的全局 Review 报告

> 审查时间：2026-05-27
> 审查范围：整个 Bocchi 项目源码、测试、文档、构建配置
> 项目状态：M0–M6 已完成，M7 进行中

## 项目概况

| 指标 | 数值 |
| --- | --- |
| C# 源文件 | 268 |
| Razor 文件 | 45 |
| C# 代码行数 | ~30,000 |
| 测试文件 | 48 |
| 测试用例 | 198（全部通过） |
| 里程碑 | M0–M6 已完成，M7 进行中 |

---

## 一、架构优点

### 1.1 三段式架构边界清晰

Home Server（内网 CMS + 构建器）、Page Frontend（静态 Theme）、Cloud Server（预留）三段职责分明。Home Server 不暴露公网 API，Theme 通过 Contract 接入，Cloud Server 只在有真实需求时才实现。这个边界从 M0 到 M6 一直没有被打破。

### 1.2 workspace / DataRoot 严格切分

用户内容（Markdown、YAML、媒体）在 `workspace/`，运行数据（SQLite、缓存、构建产物、日志）在 DataRoot 下。`BocchiDataLayout` 和 `WorkspaceLayout` 的分离保证了：

- workspace 可独立打包迁移，不依赖 Bocchi 程序
- Bocchi 被遗弃或重构时，用户内容可一键带走
- 构建产物不污染源内容目录

这是整个项目最值得肯定的架构决策。

### 1.3 ContentModel 极简设计

`Bocchi.ContentModel` 是纯数据契约层：零依赖、sealed record、值语义、不可变。13 个文件、单一命名空间，是理想的"叶节点"项目。`ContentSlug`（保留 CJK Unicode）和 `CategorySlug`（纯 ASCII）的分层设计合理。

### 1.4 Generator Pipeline 模式成熟

12 个 `IBuildStage` 固定顺序执行，每阶段职责清晰。构建指纹 + up-to-date 短路机制避免了无效重复构建。`IBuildSink` 三实现（FileSystem / HttpStream / DryRun）分别对应 FullBuild、Live 预览和测试场景。

### 1.5 软错误聚合模式

`LoadResult<T>` + `ContentValidationError` 让 Workspace 扫描不会因单条 frontmatter 错误中止整个流程。错误有 Severity / Code / Field / Message 结构化分类，Dashboard 可按严重程度展示。

### 1.6 凭据保护一致

所有敏感凭据（OAuth secret、发布 token、Git 连接凭据）统一使用 ASP.NET Core Data Protection 加密存储，解密只在操作前短暂使用。`PublishSecretSanitizer` 在错误信息和日志中做脱敏处理。代码中未发现硬编码密码或明文 token。

### 1.7 i18n 三层分离

Dashboard UI i18n（JSON 资源文件）、站点 i18n（数据库）、Theme 私有 i18n（数据库）三层分离。Common key 覆盖 → Theme 私有覆盖 → Theme manifest 默认值 → key 本身的解析链清晰。M6 落地了内容多语言版本、URL 前缀策略、hreflang 和 Sitemap 多语言条目。

### 1.8 Theme Contract 设计前瞻

`config-schema.json` 声明式配置 + Dashboard 自动生成表单，避免了手写每个 Theme 的设置 UI。`InputEnvelope` 统一封装 schema URI + contract version + timestamp + data，Theme 端可做版本对账。`ThemeResolver` 统一处理 builtIn / installed / devLink / packageCandidate 四种来源。

### 1.9 文档质量高

`Architecture.md`（743 行）覆盖了产品定位、系统组成、数据切分、构建流水线、Theme Contract、安全边界和架构护栏，每条决策都有理由。`Milestones.md` 有完整的决策记录和验收标准，可作为项目恢复上下文的入口。

---

## 二、代码缺点与技术债务

### 2.1 超大文件

| 文件 | 行数 | 问题 |
| --- | --- | --- |
| `DefaultStaticTemplateRenderer.cs` | 1540 | 远超 1000 行阈值，承担输入读取、模板模型构建、URL 相对化、资产复制等多重职责 |
| `ThemeSettingsService.cs` | 1113 | 同时承担 Theme 配置 CRUD、config-schema 解析、i18n override 管理、Page Contract 解析 |
| `ThemeInputWriter.cs` | 924 | 12 个 JSON 文件的序列化 + Navigation tree 解析 + 多语言文案 fallback 链 |
| `ContentGraphBuilder.cs` | 745 | Post/Page/Work/Note/Friend 的图构建 + Category tree + URL 策略 + Media 路径改写 |

这些文件都接近或超过 AGENTS.md 中"单页文件大于 1000 行时考虑拆分"的约定。

### 2.2 重复代码

**SHA-256 hex 编码**：至少 6 处独立实现相同的 `SHA256.HashData` + hex 编码逻辑，且存在 `Sha256Util` 和 `HashUtil` 两个功能完全相同但类名不同的工具类。`Convert.ToHexString` 在 .NET 5+ 已内置，`ContentScanner.HashFileAsync` 中已经使用了它，但其他地方没有统一。

**FlattenPostCategories 递归**：在 `ContentGraphBuilder`、`ComputeFingerprintStage`、`SitemapXmlGenerator`、`ThemeInputWriter` 中有 4 处完全相同的递归展开逻辑。

**路径解析三段重复**：`ContentEditingService` 的 `ResolveContentFile`、`ResolveContentPathForNewFile`、`ResolveContentPathForMovedFile` 中"规范化 → 检查 `..` → 拼 root → 验证 StartsWith"的模式重复了 3 次。

### 2.3 硬编码文案

- `ThemeSettingsService.CommonDisplayDefaults` 中 4 种语言的 Common i18n 文案直接写在代码中
- `EditorDraftService.CreateInitialContent` 中 `title: New Page` 等默认标题硬编码
- `blazor-error-ui` 中 "An unhandled error has occurred" 未走 i18n
- Services 层约 15 处 `InvalidOperationException` 使用硬编码中文消息
- `config-schema.json` 的 `title`/`description` 字段使用硬编码中文

### 2.4 Loader 代码重复

`PostLoader`、`PageLoader`、`WorkLoader`、`NoteLoader` 有约 30% 的相似结构（split frontmatter → check YAML → parse mapping → extract fields → build model）。`ParseStatus` 和 `ParseDateTime` 已提取为 `PostLoader` 的 `internal static` 方法被其他 Loader 复用，但放在 PostLoader 里语义不正确。

### 2.5 BuildSession._bag 弱类型

`BuildSession` 使用 `ConcurrentDictionary<string, object?>` 作为阶段间数据传递容器，编译期无法保证 key 和类型的对应关系。`BuildSessionKeys` 提供了常量 key，但类型约束只靠注释。这是一个潜在的运行时错误来源。

### 2.6 GeneratorPipeline 构造函数注入 15 个依赖

12 个 stage + store + time + logger，暗示 `GeneratorPipeline` 知道太多具体实现。当前阶段可接受，但如果 stage 数量继续增长，应考虑 `IEnumerable<IBuildStage>` + 顺序约定。

### 2.7 MainLayout 需要拆分

`MainLayout.razor`（427 行）包含了完整的 sidebar 导航、topbar、语言切换、外观切换、移动端菜单。导航 active 判断逻辑（约 15 个方法）占据了大量代码，建议抽取为 `BocchiSidebar.razor`。

### 2.8 Login.razor CSS 风格不一致

`Login.razor` 使用 Tailwind CSS class（`grid min-h-[calc(100vh-6rem)]`），而其他组件使用 BEM 鱼格的自定义 class（`bocchi-shell`、`bocchi-quick-card`）。风格不统一增加了维护成本。

---

## 三、安全风险

### 3.1 CSRF 防护缺失

大量端点 `DisableAntiforgery()`：`AccountEndpoints` 中的 Setup、Login、External Login 端点，以及 `DashboardHomeEndpoints`、`DashboardLocalizationEndpoints`、`BuildEndpoints` 中的 POST 端点。虽然这些是 API-style 端点，但禁用 CSRF 保护需要确保有其它防护机制（SameSite cookie、Origin 检查）。

### 3.2 登录无账户锁定

`SubmitLoginAsync` 使用 `lockoutOnFailure: false`，攻击者可以无限次尝试密码。对于单管理员系统，建议至少在多次失败后加入延迟或 CAPTCHA。

### 3.3 外部登录自动绑定

`CompleteExternalLoginAsync` 中，如果外部登录的 email 匹配到已有用户，会自动绑定并登录。如果攻击者控制了一个与管理员相同 email 的外部账号，就能获得管理员访问。建议增加管理员确认步骤。

### 3.4 Setup 密码经过 Cookie

`SetupPendingAdminCookie` 把密码通过 Data Protection 保护后存入 Cookie（20 分钟有效期）。虽然有加密和过期机制，但密码出现在 Cookie 中仍有被日志捕获的风险。建议改为服务端 TempData。

### 3.5 html filter 安全前提

`DefaultStaticFluidRenderer` 注册的 `html` filter 将输入标记为不转义。模板中大量使用 `{{ content | html }}`。如果模板编写者不小心引入了用户可控的未转义内容，可能导致 XSS。应在文档中明确标注安全前提。

### 3.6 LibGit2Sharp 同步 I/O

`LibGit2ContentRepository` 所有方法内部是同步的 LibGit2Sharp 调用，`PullFastForwardAsync` 和 `CloneIntoEmptyWorkspaceAsync` 涉及网络 I/O 但会阻塞调用线程。在 ASP.NET Core 请求管线中可能导致线程池饥饿。

### 3.7 Generator → Theme.DefaultStatic 硬依赖

`Bocchi.Generator.csproj` 直接引用了 `Bocchi.Theme.DefaultStatic`，Generator 无法脱离 DefaultStatic Theme 编译。这破坏了 Generator 的纯净性，未来 Theme 生态扩展会成为编译时包袱。

---

## 四、测试评估

### 4.1 覆盖情况

| 测试项目 | 用例数 | 覆盖评价 |
| --- | --- | --- |
| Bocchi.ContentModel.Tests | 8 | **不足** — 只验证枚举和基本构造，未覆盖 ContentSlug 边界情况 |
| Bocchi.GeneratorContract.Tests | 8 | 基本覆盖 |
| Bocchi.Workspace.Tests | 37 | 良好 |
| Bocchi.Generator.Tests | 42 | **良好** — 端到端测试覆盖核心构建路径、安全边界、SEO 输出 |
| Bocchi.HomeServer.Tests | 103 | **良好** — 覆盖 Setup、授权、设置、内容编辑、发布、预览 |

### 4.2 测试空白

- **Theme 渲染器无独立单元测试**：`DefaultStaticTemplateRenderer`、`DefaultStaticFluidRenderer`、`DefaultStaticInlineTextRenderer` 只通过端到端测试间接覆盖。`InlineTextRenderer` 的 `[color=...]` 解析有多个边界情况值得单独测试。
- **DefaultStaticThemeText 无单元测试**：三层文案优先级、语言回退顺序只通过端到端验证。
- **ContentModel 测试过少**：8 个用例不足以覆盖 ContentSlug 的边界（空字符串、超长输入、特殊字符、CJK 混合）。

### 4.3 测试基础设施

`TestWorkspaceFixture` 构造真实 workspace + 完整 DI 容器，避免了过度 mock。`TempThemeDataRoot`、`TempPackageDataRoot` 确保测试隔离。这是好的实践。

---

## 五、架构护栏遵守情况

| AGENTS.md 约定 | 遵守情况 |
| --- | --- |
| 积极写注释 | **良好** — zh-CN 注释覆盖充分，设计决策有说明 |
| 避免过度封装 | **良好** — 没有发现只调用一次的过度抽象 |
| 不读 `**/Vibe/` | 已遵守 |
| 单页 >1000 行考虑拆分 | **违反** — 4 个文件超过阈值（DefaultStaticTemplateRenderer 1540、ThemeSettingsService 1113、ThemeInputWriter 924、ContentGraphBuilder 745） |
| UI 文本走 i18n | **部分违反** — Dashboard 主体已走 i18n，但有约 15 处硬编码中文异常消息和若干硬编码英文 UI 文本 |
| 禁止手写 EF migration | 已遵守 — 只有一个 EF Core CLI 生成的 migration |
| 禁止临时文案写入 UI | 已遵守 |

---

## 六、优先改进建议

### 高优先级

1. **统一 SHA-256 hex 编码**：消除 `Sha256Util` / `HashUtil` 重复，统一使用 `Convert.ToHexString`
2. **拆分 `DefaultStaticTemplateRenderer.cs`**（1540 行）：至少拆为 InputReader、ModelBuilder、Renderer
3. **拆分 `ThemeSettingsService.cs`**（1113 行）：拆出 ConfigSchemaService 和 I18nService
4. **提取 `FlattenPostCategories` 为共享扩展方法**：消除 4 处重复

### 中优先级

5. **为 `DefaultStaticInlineTextRenderer` 和 `DefaultStaticThemeText` 添加单元测试**
6. **将 Generator → DefaultStatic 硬依赖改为 DI 注入**
7. **登录端点加入失败延迟或锁定机制**
8. **Services 层硬编码中文异常消息改为 i18n key 或至少统一到常量**

### 低优先级

9. **MainLayout 拆分 sidebar 为独立组件**
10. **Login.razor CSS 风格统一**
11. **ContentModel 测试补充边界情况**
12. **Loader 重复代码提取为基类或工具方法**
13. **config-schema.json 的 title/description 支持 i18n**

---

## 七、总结

Bocchi 截止 M6 的整体代码质量**高于平均水平**。最值得肯定的五个决策：

1. **workspace / DataRoot 切分** — 用户内容独立于程序命运
2. **ContentModel 纯数据契约** — 零依赖、record 语义、不可变
3. **构建指纹 + 短路** — 避免无效重复构建
4. **软错误聚合** — 不因单条错误中止整个扫描
5. **i18n 三层分离** — Dashboard / 站点 / Theme 各司其职

最需要关注的三个风险：

1. **安全** — CSRF 防护缺失 + 登录无锁定 + 外部登录自动绑定
2. **大文件** — 4 个文件接近或超过 1000 行阈值，维护成本上升
3. **重复代码** — SHA-256 编码、路径解析、Category 树展开等处有明显重复

项目处于开发早期阶段，这些技术债务在可控范围内。建议在进入 M7 之前，先处理高优先级改进项，特别是安全相关的登录锁定和 CSRF 防护。
