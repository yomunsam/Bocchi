# Fluid Static 与内置 Theme 边界设计

日期：2026-06-18

## 1. 决策

采用“公共 Fluid Static 实现与具体内置 Theme 分离”的方案。

`fluid-static` 是 Bocchi 支持的 Theme 实现方式之一，不是所有 Theme 的基础类，也不限制第三方作者选择其他技术栈。第三方 Theme 只要遵守 Theme Contract 的文件输入、输出与打包边界，就可以使用 `process` Runner 接入 Node.js、Hugo、Rust binary 或其他任意实现。

选择 `fluid-static` 的 Theme 作者不需要安装、引用或编写任何 .NET 代码。Theme 源码只包含 JSON manifest/schema、Liquid 模板和 CSS、JavaScript、图片、字体等静态资源。Bocchi 宿主内部使用 .NET 执行 Fluid renderer，但这不是 Theme 作者的开发依赖。

`Bocchi Mono` 和 `Cozy` 都是 `fluid-static` 的消费者。Cozy 是独立第三方 Theme 的参考实现，在源码、资源和运行行为上不得依赖 Bocchi Mono。

## 2. 目标

- 明确 Theme Contract、Fluid Static 实现和 Bocchi Mono 的职责边界。
- 将默认 Theme id 从 `default-static` 直接改为 `bocchi-mono`。
- 让 Cozy 在完全没有 Bocchi Mono 模板或资源的情况下独立构建。
- 将 Fluid Static 的模板文件、Liquid model、filters、路由和错误行为定义为可测试的公开 profile。
- 消除 `DefaultStatic*`、`theme.defaultStatic.*` 等具体 Theme 命名对公共实现的污染。
- 在对外试用前形成可冻结的 Fluid Static v1 行为。

## 3. 非目标

- 不把 Fluid/Liquid 规定为第三方 Theme 的唯一实现方式。
- 不建立动态 Runner plugin、独立 NuGet SDK 或通用插件市场。
- 不为 `default-static` id、旧 namespace 或旧 Theme 私有 i18n key 编写兼容层。
- 不要求第三方 Theme 引用 Bocchi 的 .NET assembly。
- 不让 Theme 直接访问数据库、Home Server UI 状态或最终发布目录。

## 4. 项目与依赖边界

### 4.1 Bocchi.GeneratorContract

继续承载与实现语言无关的公开文件契约：

- `theme.json` manifest。
- Theme input JSON 文件及其版本。
- `config-schema.json` schema。
- Theme feature、static assets、i18n、page template 和 special page 声明。
- Theme Contract version。

它不引用 Fluid Static 或任何具体 Theme。

### 4.2 Bocchi.Theme.FluidStatic

由现有 `Src/Themes/Bocchi.Theme.DefaultStatic` 重命名并收敛而来，只承载公共 Fluid Static profile：

- Theme Contract 输入读取。
- 标准静态路由生成。
- Liquid page model 组装。
- Fluid parser、公开 filters 与 HTML encoding 边界。
- 多语言、SEO、文章时间和 navigation model。
- 站点根相对 URL 到输出页面相对 URL 的改写。
- Fluid Static 通用前端 runtime 的输出。
- Fluid Static 专属异常与诊断。

该项目不得包含：

- `bocchi-mono` 或其他具体 Theme id。
- 具体 Theme 的 manifest、schema、模板、CSS、JavaScript 或图片资源。
- `theme.bocchi-mono.*` 等具体 Theme i18n key。
- 缺少模板时回退到 Bocchi Mono 的逻辑。

主要类型采用 `FluidStatic*` 命名，例如 `FluidStaticRenderer`、`FluidStaticRenderRequest`、`FluidStaticTextResolver` 和 `FluidStaticException`。

### 4.3 Bocchi.Theme.BocchiMono

新增小型具体项目，职责仅限于随 Bocchi 分发内置 Theme：

- 嵌入 `Themes/bocchi-mono`。
- 提供 `BocchiMonoThemeDefinition`。
- 把缺失的内置 Theme 文件物化到 `<data>/themes/bocchi-mono/`。
- 保证 embedded resource 路径在 Windows 与 Unix 上使用一致的逻辑分隔符。

它不实现 Liquid renderer，不为其他 Theme 提供模板回退。

### 4.4 Themes/bocchi-mono

它是一个具体、完整、自包含的 Fluid Static Theme：

- manifest id 为 `bocchi-mono`。
- 展示名为 `Bocchi Mono`。
- 私有 i18n key 使用 `theme.bocchi-mono.*`。
- 包含完整的必需模板和自身静态资源。
- 不拥有或定义 Fluid Static 的公共行为。

### 4.5 bocchi-theme-cozy

Cozy 保持独立仓库，作为真正第三方作者可以复制和学习的 demo：

- manifest id 保持 `bocchi-theme-cozy`。
- 私有 i18n key 使用 `theme.bocchi-theme-cozy.*`。
- 包含完整的必需模板和自身静态资源。
- 不包含 `default-static`、`DefaultStatic` 或 `bocchi-mono` 引用。
- 不需要 .NET、Node.js 或 Theme 自身的 build step。

Cozy 只依赖 Bocchi 对外承诺的 Theme Contract 与 Fluid Static v1 profile。

## 5. Runner 模型

Theme Contract 保持两类本地 Runner：

- `fluid-static`：Bocchi 内置的无外部工具链静态渲染方式。Theme 作者编写 Liquid 与静态资源。
- `process`：Bocchi 启动 Theme 声明的外部命令，作者自由选择实现技术。

两类 Runner 都遵守同一文件边界：

1. Generator 写入 Theme input directory。
2. Runner 只读取 Theme Root 与 input directory。
3. Runner 只写 Theme local output directory。
4. Generator 校验并收集输出到最终发布目录。

普通 Preview、Full Build、zip inspection 和 Theme 安装都不会替第三方 Theme 自动安装依赖。

## 6. Fluid Static v1 profile

### 6.1 路由与模板

Fluid Static 继续生成标准博客路由，包括首页、文章、独立页面、作品、短文、友链和 404。

标准路由对应的模板是 Theme 自身的必需文件。缺失时构建必须失败并报告相对路径，不允许从 Bocchi Mono 补齐。

自定义独立页面模板 `standalone-page-{name}.liquid` 缺失时，可以回退到同一个 Theme 的 `standalone-page.liquid`。这是 Theme 内部 fallback，不涉及其他 Theme。

### 6.2 Liquid model

Fluid Static v1 冻结并文档化当前模板使用的公共 model，包括：

- `site`
- `page`
- `localization`
- `navigation`
- `home`
- `hero`
- `items` 与各内容集合
- `section`
- `previous` / `next`
- `theme.config`

Theme 的有效配置以通用对象暴露在 `theme.config` 下。Fluid Static 可以定义少量标准 profile 配置语义，但不能阻止 Theme 声明和读取额外字段。

### 6.3 i18n

Common 文案继续使用 `common.*`、`content.*` 和 `menu.*`。

Theme 私有文案由 Theme 自己在 manifest 声明，并使用 `theme.<theme-id>.*` namespace。Package inspection 应拒绝不属于当前 Theme namespace 的 Theme 私有 key，避免不同 Theme 共享或覆盖彼此私有文案。

Fluid Static 提供通用翻译访问方式，不再把固定 Theme 私有 key 映射为 `text.homeSelectedWriting` 等公共实现字段。Liquid 模板可以通过公开 `t` filter 按 key 读取当前语言文案。

### 6.4 通用前端 runtime

当前 Bocchi Mono 与 Cozy 之间大段重复的前端行为拆成 Fluid Static runtime。它只处理跨 Theme 的协议行为：

- 语言状态与文案切换。
- appearance 状态。
- `bocchi-time`。
- Fluid Static 约定的 `data-*` hook。

Renderer 将该 runtime 写入稳定的 `_bocchi` 输出路径，Theme 通过公开 model 提供的 URL 引用它。Theme 自己的 Lucide、动画、布局交互和视觉逻辑仍保留在自身 `assets/app.js`。

生成 runtime 属于 Fluid Static profile 依赖，不属于 Bocchi Mono 依赖；最终发布目录仍然是完全静态、可独立部署的。

## 7. 错误边界

构建与内置 Theme 物化失败信息至少区分：

- manifest 或 contract 不支持。
- 必需模板缺失。
- Liquid parse 失败。
- Liquid render 失败。
- input 文件缺失或格式错误。
- Theme 输出路径非法。
- Bocchi Mono 物化阶段的 embedded resource 缺失。

错误使用稳定类别和可定位的相对路径。Theme 错误不得伪装成 Bocchi Mono 错误，第三方 Theme 构建不得读取其他 Theme 的文件作为恢复手段。

## 8. 命名与兼容性

当前仍处于对外试用前阶段，实施直接终态改名：

- `default-static` → `bocchi-mono`。
- `Bocchi.Theme.DefaultStatic` → `Bocchi.Theme.FluidStatic`。
- 具体内置资源代码进入 `Bocchi.Theme.BocchiMono`。
- `DefaultStatic*` 公共实现类型 → `FluidStatic*`。
- `theme.defaultStatic.*` 分别改为各 Theme 自己的 namespace。

不保留 alias，不迁移旧本地测试数据，不增加双读或双写。改名与边界修正完成后，再把 Theme Contract `1.0` 作为对外试用基线冻结。

## 9. 验证策略

### 9.1 Contract tests

- 验证 `fluid-static` 与 `process` manifest。
- 验证 Theme 私有 i18n namespace。
- 验证 Fluid Static v1 必需模板清单。
- 验证不支持的 Contract version 和 runner 会被拒绝。

### 9.2 Fluid Static tests

- Renderer 测试项目只引用 GeneratorContract 与 FluidStatic，不引用 BocchiMono。
- 覆盖输入读取、model、filters、URL 相对化、多语言、SEO 和 runtime 输出。
- 缺少必需模板时验证明确失败。
- 验证自定义独立页面模板只在当前 Theme 内 fallback。

### 9.3 Bocchi Mono tests

- 验证 `Themes/bocchi-mono` 能被嵌入和物化。
- 验证 Windows 与 Unix 风格路径都能定位 embedded resource。
- 验证已存在的用户文件不会被覆盖。

### 9.4 Theme conformance

使用同一组代表性 Theme input 分别构建 Bocchi Mono 与 Cozy：

- 两个 Theme 都产生完整标准路由。
- 两者的 i18n、config 和 assets 相互隔离。
- Cozy 构建过程不需要物化或读取 Bocchi Mono。
- Cozy 仓库中不存在 Bocchi Mono 或 Default Static 引用。

## 10. 完成标准

- 项目和类型命名能直接表达公共实现与具体 Theme 的区别。
- 删除 Bocchi Mono 资源后，Cozy 仍可完整构建。
- `Bocchi.Theme.FluidStatic` 的程序集依赖中不存在 `Bocchi.Theme.BocchiMono`。
- Bocchi Mono 与 Cozy 都是完整、自包含的 Fluid Static Theme。
- 第三方作者只根据文档和 Cozy demo 即可创建 Theme，不需要阅读 Bocchi Mono 或 .NET renderer 源码。
- Windows 上的 embedded resource 路径问题得到验证。
- solution build、相关测试和 `git diff --check` 通过。
