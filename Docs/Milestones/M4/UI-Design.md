# M4 UI Design Baseline

本文档是 M4-T01 的设计基线。它把 2026-05-14 确认的 Dashboard 视觉方向、布局规则、组件风格、移动端模式和后续实现护栏固定下来，后续 M4 页面实现、截图验收和 UI 评审都以本文为准。

关联入口：

- M4 作战图：`Docs/Milestones/M4/M4.md`
- 项目局部 skill：`.codex/skills/bocchi-ui-style/SKILL.md`
- 已确认效果图：`Docs/Milestones/M4/Assets/m4-ui-style-direction-2026-05-14.png`

## 1. 最终方向

Bocchi Admin 不是 GitHub 风格的仓库面板，也不是企业 CMS 或硬核运维工作台。它应当是一个柔和、轻量、低到中信息密度的个人发布 App：让普通用户能舒服地写 Blog、整理页面、预览站点、发布内容，并在必要时理解构建状态。

目标受众按 18-35 岁个人创作者设定，兼顾习惯电脑工作的人和更熟悉手机 App 的人。界面应当有年轻、柔软、亲近的气质，但不能牺牲可读性、可维护性和长期使用效率。

视觉灵感可以保留 `Bocchi` 这个名字带来的抽象气质：害羞但明亮、个人创作、卧室音乐/写作感、柔和粉蓝、轻微 anime-inspired mood。禁止直接复制任何可识别的动漫角色、脸、发型、服装、乐器姿势、乐队标识、角色名或具体场景。

## 2. 已否定方向

以下方向已经在效果图阶段被否定，后续不要回退：

- 类 GitHub 的单色仓库面板。
- 太正式、太专业的工作台气质。
- 主要内容区过密、表格列太多、底部堆很多小信息框。
- Overview 直接展示硬核 Log / terminal / command output。
- 粉色饱和度过高、刺眼或变成主视觉噪声。
- 桌面布局完全依赖宽表格，几乎无法自然变成移动端。

## 3. 视觉关键词

保留：

- 柔和
- 轻量
- App-like
- 个人发布工具
- 年轻但成熟
- 低到中信息密度
- 粉蓝低饱和
- 普通用户能理解

避免：

- 企业后台
- 运维控制台
- 开发者工具
- 营销 Landing Page
- 玩具感
- 玻璃拟态
- 大面积渐变
- 大面积深蓝/深灰
- 密集分析图表

## 4. 色彩与主题

Dashboard 外观模式默认为 `Auto`，由系统偏好决定 Light / Dark；用户可以在右上角用紧凑下拉选择 `Auto`、`Light`、`Dark`。这个控件只负责后台 dark mode / light mode，不叫 `Theme`，不要和前台业务 Theme Contract 混用，也不要再使用占宽的 `Light / Dark / Auto` 分段控件。

### 4.1 Light Token 建议

| Token | 用途 | 建议色值 |
| --- | --- | --- |
| `--bocchi-bg` | 页面背景 | `#F8F7FC` |
| `--bocchi-surface` | 主表面 | `#FFFFFF` |
| `--bocchi-surface-soft` | 柔和提示背景 | `#F2F7FF` |
| `--bocchi-surface-pink` | 少量粉色提示背景 | `#FFF3F8` |
| `--bocchi-text` | 主文字 | `#26253A` |
| `--bocchi-text-muted` | 次要文字 | `#747186` |
| `--bocchi-border` | 边框 | `#E8E4EF` |
| `--bocchi-accent-blue` | 主要动作 / focus | `#7CB7F8` |
| `--bocchi-accent-pink` | 品牌辅助色 | `#EFB3C9` |
| `--bocchi-accent-lavender` | 次级选中态 | `#DCD7FA` |
| `--bocchi-success` | 成功状态 | `#78C6A3` |
| `--bocchi-warning` | 轻警告 | `#E8BE6A` |
| `--bocchi-danger` | 错误状态 | `#E98989` |

粉色只能作为辅助色和局部状态，不作为大面积背景。若页面第一眼被粉色抓住，说明饱和度或面积过高。

### 4.2 Dark Token 方向

Dark 不是黑色终端主题，而是“夜间写作房间”的低刺激版本：

- 背景偏温深灰紫，不要纯黑。
- 表面分层靠明度差和细边框，不靠强蓝色。
- 粉蓝色继续降低饱和度，作为轻强调。
- 状态色要可辨认，但避免荧光感。

Dark 的精确色值可在 M4-T06 实现时通过截图微调，但必须与 Light 共用同一套语义 token。

## 5. 布局规则

### 5.1 桌面端

桌面端采用可响应的 App Shell：

- 左侧一级导航可以展开为窄侧栏，未来移动端可自然折叠。
- 左上角 Logo 只显示文字 `Bocchi`，不要 icon、头像、吉祥物或图形标。
- 顶栏包含搜索/快速创建、Dashboard 外观下拉、账号菜单。外观下拉应紧凑。
- 主内容区优先使用单主列布局，必要时右侧只放一个轻量 helper/status 面板。
- 不在底部堆叠多个小框框。
- 不使用宽而密的专业表格作为默认内容列表。

### 5.2 移动端

移动端不是事后压缩桌面表格，而是同构的单列 App 体验：

- 一级导航收起为顶部菜单、底部菜单或抽屉。
- 二级导航优先变成下拉、tabs 或横向 chips。
- 内容项是单列 row/card，包含标题、类型、路径/日期、状态、一个主操作。
- 次要操作收进 `...` 菜单。
- Markdown Editor 使用 `Edit / Preview / Metadata` 模式切换，窄屏不强行左右分栏。

## 6. 组件基线

### 6.1 Content List

内容列表是 M4 的主体验锚点。默认样式应像 App 上的轻列表，而不是数据后台表格。

每条内容建议包含：

- 标题。
- 类型 chip：Post / Page / Work / Note / Friend / Site。
- 简短路径或更新时间。
- 人类可读状态：`Draft`、`Ready`、`Needs a quick look`、`Published`。
- 一个明显主动作：`Continue`、`Preview`、`Edit`。
- 其他动作进入菜单。

宽表格只保留给高级管理或诊断页面，不作为普通 Content 入口的默认模式。

### 6.2 Publish / Build

M3 的构建能力在 M4 UI 中面向普通用户时应表达为发布/检查状态，而不是 Log 区域。

Overview 或 Publish 首页优先展示：

- `Publish looks okay`
- `Media check passed`
- `One link needs review`
- `Last preview is ready`
- `Open advanced log`

原始日志、manifest、artifact tree 和阶段输出仍然需要存在，但放到 Build 详情或 Advanced Log 中，不能占据普通用户的首页视觉中心。

### 6.3 Markdown Editor

编辑器以 Markdown 为核心，但第一眼不能像 IDE：

- 桌面端可以是 Markdown + Preview 分栏。
- Metadata / frontmatter 可以是右侧 drawer、侧栏或独立 tab。
- 保存、预览、查看 diff 是清晰主动作。
- 技术错误需要翻译成人能处理的提示，再提供高级细节。

### 6.4 Setup / Login

Setup 和 Login 要延续柔和 App 风格，但保持安全感：

- Setup 是初始化流程，不是营销欢迎页。
- 第一个 Admin 创建流程要清楚、短、不可误解。
- 外部登录按钮只显示已启用且配置完整的 Provider。
- 错误提示要具体，不使用堆栈或框架错误原文。

### 6.5 Preview Toolbar

Preview Toolbar 要轻、可收起、不遮挡正文：

- 默认显示 `Preview` 状态、当前 route、返回 Admin。
- 能定位文章 / 页面 / 作品时显示 `Edit`。
- 不能定位内容时不要硬给编辑入口。
- 移动端必须可收起，并避开正文核心区域。

## 7. 导航与信息架构

一级导航建议改成更接近普通用户心智的命名：

- Home
- Write
- Content
- Preview
- Publish
- Settings

这不否定 M4 原有的系统边界：Build 仍然存在，但普通入口优先叫 Publish；高级页中可以出现 Build Runs、Artifacts、Advanced Log。

Settings 的二级分类保持：

- Basic Info
- Database
- Third-party Login
- Theme
- Users
- Workspace

桌面端可以展示二级导航；移动端必须有下拉或 tabs 模式，不允许依赖固定双侧栏。

## 8. 截图与验收视口

M4-T06 之后每个主要页面都应至少检查这些视口：

- `1440x900`：桌面主工作区。
- `1024x768`：窄桌面 / 平板横屏。
- `768x1024`：平板竖屏。
- `412x915`：手机。

检查问题：

- 主内容是否仍是可读单列或清晰布局。
- 文本是否溢出按钮、chip、列表 row、侧栏。
- Dashboard 外观下拉是否占用过多顶栏空间。
- 粉色是否过饱和。
- 是否又出现过多小框框。
- 普通首页是否出现硬核日志。

## 9. 后续任务影响

- M4-T06 Dashboard Shell：应先实现本设计基线里的 App Shell、Dashboard 外观下拉、低密度导航和响应式骨架。
- M4-T07 内容列表与详情入口：默认使用 App-like list，不使用宽密表格作为主入口。
- M4-T08 Markdown 编辑器：桌面分栏，移动端模式切换；避免 IDE 化。
- M4-T09 设置面板：保留清晰分组，但不要变成密集系统控制台。
- M4-T10 构建日志与产物面板：普通入口改为 Publish/检查状态；原始 Log 进入高级详情。
- M4-T11 前台预览与浮动工具栏：轻量、可收起、可移动端使用。

## 10. 评审清单

每次 UI 设计或实现结束前自查：

- 它更像个人发布 App，而不是专业工作台吗？
- 主内容区是不是已经做了减法？
- 内容列表能自然变成手机单列吗？
- 页面里有没有过多小信息框？
- 粉色是不是柔和、低饱和、面积受控？
- Dashboard 外观 / dark mode 切换是不是紧凑下拉，并且没有和前台业务 Theme 选择混在一起？
- Logo 是否仍然只是 `Bocchi` 文字？
- 技术状态是否翻译成普通人能理解的语言？
- 是否完全避开了可识别动漫角色、服装、姿势和标识？

## 11. 2026-05-15 Dashboard 精修规则

M4 功能闭环完成后，Dashboard 进入正式交付精修。后续实现默认沿用以下追加规则：

- 不在正式 Dashboard 顶栏保留不可用的 disabled search、占位按钮或“以后会做”的控件；没有实现的能力不占据主视觉。
- Dashboard 背景保持安静，不使用装饰性径向光斑、bokeh 或大面积渐变来制造氛围。
- Home 首页按正式 Overview 组织：主区是可扫读的 content feed，右侧是 Site preview 与 Publish readiness；Server / 数据库 / 日志等运维信息默认退到辅助区域或高级详情。
- 顶栏优先提供真实可用的 Write、Content、Preview 快捷入口；Dashboard 外观切换仍保持紧凑下拉。
- 每一轮 UI 精修都要检查 hover、focus-visible、移动端单列折叠和长路径换行，避免页面看起来只是功能原型。

### 11.1 图标与样式工具选择

- Dashboard 图标使用 `Blazicons.Lucide`，作为 Blazor 侧正式 icon library；导航、顶部动作、内容类型、预览和发布检查都应优先使用 Lucide 图标，不再用纯文字堆导航。
- 暂不引入 Tailwind CSS 构建链。当前 Home Server 没有 npm / PostCSS pipeline，直接接 Tailwind 会先制造额外构建维护成本；M4 精修先用语义 CSS token、Blazor scoped CSS 和 Lucide 图标把视觉质量拉到交付线。
- 如果后续 M4/M5 页面数量继续扩大，再评估 Tailwind 或 Blazor UI framework，但必须连同构建命令、watch/dev 体验和发布产物一起纳入设计，而不是只为了某一屏加依赖。
- Overview 的正式结构对齐效果图：左侧 icon nav、顶部 search + New + appearance + account、主区 content feed、右侧 Site preview 和 Publish readiness；Server / 数据库 / 日志信息退到 sidebar 或高级详情。
