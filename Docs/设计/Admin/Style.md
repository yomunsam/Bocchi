# Bocchi Admin 视觉与样式规范

> 仅适用于 `Bocchi.HomeServer` 后台（`/Admin/*`、`/Setup`、`/Account/*`）。前台由独立 Theme 决定，**不**受本规范约束。

## 1. 心智模型

- **私域工作台**，不是公开站点。允许使用粉、紫等"产品级"暖色，避免炫技。
- 信息层级要"轻、轻、再轻"：用留白与字重区分主次，谨慎使用边框和阴影。
- 任何新组件在落地前先问一次：能不能复用现有 `bocchi-*` 类？能复用就不要新增。

## 2. 设计 Token（`wwwroot/app.css` 顶部 `:root`）

| 用途 | 浅色 token | 深色覆盖 |
| --- | --- | --- |
| 页面背景 | `--bocchi-bg` | 同名 |
| 卡片背景 | `--bocchi-surface` | |
| 中性悬停 | `--bocchi-surface-muted` | |
| 弱蓝面板 | `--bocchi-surface-soft` | |
| 弱粉面板（CTA、Note） | `--bocchi-surface-pink` | |
| 主文本 | `--bocchi-text` | |
| 次文本 | `--bocchi-text-muted` | |
| 弱文本（meta/eyebrow） | `--bocchi-text-subtle` | |
| 边框 | `--bocchi-border` | |
| 主操作色（草莓粉） | `--bocchi-action` | |
| 蓝色强调 | `--bocchi-blue` / `--bocchi-blue-strong` | |
| 状态绿/黄/红 | `--bocchi-success-*` / `--bocchi-warning-*` / `--bocchi-danger-*` | |
| 软阴影 | `--bocchi-shadow-soft` | |
| 焦点环 | `--bocchi-focus-ring` | |
| 侧栏激活底色 | `--bocchi-nav-active` | |

新增颜色时**必须**同时在 `:root` 和 `:root[data-bocchi-effective-appearance="dark"]` 注册。避免在组件里硬编码 16 进制色。

## 3. 间距 / 圆角 / 字号刻度

- 间距使用 0.2 / 0.4 / 0.55 / 0.7 / 0.9 / 1 / 1.3 / 1.75rem 作为锚点，必要时配 `clamp()`。
- 圆角：
    - 5px：极小标记（kbd、tiny dot）
    - 8–10px：输入框、列表行、传统卡片
    - 14px：大卡片（Guide / Quick action / Composer / Preview）
    - 999px：Pill / 头像 / 状态徽章
- 字号：H1 `clamp(1.7rem, 2.4vw, 2.1rem)`；H2 `1.0–1.1rem`；正文 `0.92–0.95rem`；meta `0.78–0.85rem`。
- 字重等级只允许 500 / 600 / 620 / 720 / 760 / 780 几个挡位。不要使用 400 或 900。

## 4. 命名约定

- **块级组件根**：`.bocchi-<name>`（`bocchi-composer`、`bocchi-guide-stack`）。
- **元素**：`__elem`（`bocchi-quick-card__icon`）。
- **变体 / 状态**：`--mod`（`bocchi-quick-card__icon--pink`、`bocchi-composer__tab--active`）。
- 不要在 `wwwroot/app.css` 里以"页面名"命名（`/Admin/Home/...`），用组件命名。
- 组件级局部样式优先放在 `*.razor.css`（自动 scoped）；可在多页复用的样式留在 `wwwroot/app.css` 或 `wwwroot/css/admin-home.css`。

## 5. 现成可复用类

| 类 | 作用 |
| --- | --- |
| `.bocchi-shell.bocchi-shell--dashboard` | Dashboard 通用页面容器（垂直主轴 1rem gap） |
| `.bocchi-page-heading` / `.bocchi-page-intro` | 页头：粗体 + 描述（前者带 actions 列，后者紧凑） |
| `.bocchi-eyebrow` | uppercase 小字 kicker |
| `.bocchi-button` / `.bocchi-button--primary` | 主按钮 / 粉色 CTA |
| `.bocchi-link-pill` | 弱 CTA pill（"查看全部"、"去设置"） |
| `.bocchi-status-pill` + `--neutral/info/success/warning/danger` | 状态徽章 |
| `.bocchi-banner.bocchi-banner--warning` | 警告条 |
| `.bocchi-content-list` + `.bocchi-content-item` + `__icon--pink/lavender/blue/amber` | 内容列表行 |
| `.bocchi-home-empty` | 空状态卡 |
| `.bocchi-card-heading` | 卡片标题（图标 + 标题 + 右侧链接） |
| `.bocchi-preview-card` | 通用预览卡容器 |
| `.bocchi-pill-control` / `.bocchi-icon-button` / `.bocchi-account-avatar` | 顶栏圆形/胶囊控件 |
| `.bocchi-quick-card` + `__icon--pink/lavender/blue/green` | 首页快捷动作卡 |
| `.bocchi-guide-stack` | 引导卡堆栈 |
| `.bocchi-composer` | 首页发布区 |

## 6. 组件落地规则

1. 优先用 `Components/Ui/*` 里现成 Blazor 组件（`BocchiListRow`、`BocchiStatusPill`、`BocchiContentKindIndex` 等）。
2. 添加新 UI 组件时：放到 `Components/Ui/`、用 `Bocchi*` 前缀、自带 `.razor.css`。
3. 全局新样式段要写一条 `/* ============ 区块名 ============ */` 注释，便于后续 split。
4. **不要**直接覆盖 `button { ... }` 这种 element 选择器。全局已有，组件用类名补差异。
5. 颜色用 `color-mix(in srgb, var(--token) <pct>%, ...)` 调浓淡；不要直接 `rgba`。

## 7. 文件组织

```
Src/HomeServer/Bocchi.HomeServer/
├── Components/Layout/MainLayout.razor[.css]   ← Admin 外壳：侧栏 + 顶栏
├── Components/Ui/*                            ← 可复用 Blazor 组件
├── Components/Pages/*                         ← 路由页面
└── wwwroot/
    ├── app.css                                ← Tokens + 通用控件 + 共享 Dashboard 类
    └── css/admin-home.css                     ← 首页大组件（guide / quick / composer / recent）
```

`app.css` 单文件不允许突破 2000 行。每过一档（500/1000/1500）需要审视是否拆出子文件。

## 8. 视觉验收清单

提 PR 前自检：

- [ ] 浅 / 深双主题切换无错位、无遗漏（顶栏 SunMoon 切换观察）。
- [ ] 全部交互元素有 `:focus-visible` 焦点环。
- [ ] 文本对比度 ≥ WCAG AA（深色背景 + `--bocchi-text-subtle` 经常踩线，注意）。
- [ ] 移动端窄屏（≤ 860px）侧栏退化为水平栅格，首页 grid 自动堆叠。
- [ ] 不引入硬编码颜色 / 阴影 / 字号——全部走 token。
