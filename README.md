# Bocchi 

我的新的博客系统项目。Since 2025

我们之前把博客改成了Blazor WebAssembly，后来发现不好用，因为没有静态页面，搜索引擎无法爬取。

所以我们再做一遍。


本次项目分为三个部分：

1. 博客前端
2. Home Server
3. Remote Server

现在的设计和传统的博客有了一些变化，我们希望加入一些新的功能：
1. RSS
2. 评论
3. 短文（类似Twitter、Mastodon）
4. ActivityPub（待定）

## Home Server

Home Server是整个博客系统的数据和管理的主系统，这里会存储文章和页面的图文数据，也就是说理论上我们只需要备份Home Server就可以了。

这个部分在我们现在的构想中，他可以部署在局域网、个人PC等不方便直接作为服务器的地方，也没有全天在线的要求，只要我们在编辑和发布文章的时候能够访问到就可以了。

Home Server将包含一个Dashboard，用于管理文章、页面、评论等数据。

（预计Home Server可以做一个包含Remote Server的Api功能，以便我们直接把Home Server部署到公网，但这部分优先级不高。）

<br>

## Remote Server

Remote Server是博客系统中实际在公网中承载访问业务的服务器部分。

我们把WP等传统CMS系统中的Server拆分成了Home Server和Remote Server，主要是为了尽可能精简实际部署在公网上的Server的功能，进而节省成本。

对于博客或者说一个个人主页这样的小型网站，它的实际运维成本越低，越有可能被更多人长期使用。

根据构想，Remote Server应该可以在各种Serverless平台上部署并以最低成本运行。

理论上对于一个单纯的博客来讲，理想情况下绝大多数业务都是静态的，Remote Server只处理极少数不得不用到服务器的业务，比如评论、短文、ActivityPub（待定，优先级靠后）等。

<br>

## 博客前端

这里已静态页面为主，能直接部署在各种静态网站托管服务上，被搜索引擎直接爬取的静态页面，并把不得不动态的部分通过嵌入Web App Component的方式向Remote Server请求。

理论上，Home Server和Remote Server两者相对而言是一体的，共同形成类似于Headless CMS概念的东西。而前端部分可以随便换实现方式。

理论上，可以在Remote Server离线的情况下，公网正常访问博客的基础功能（正常浏览文章，而评论和短文列表等功能不可用）。

<br>

## 大致的功能处理方式

### 博客文章、页面图文

1. 在Home Server的Dashboard上编辑文章、页面（Markdown或其他格式）
2. 保存时，由Home Server解析并预处理（段落拆分、摘要、多媒体处理等）为中间格式。
3. Home Server唤起前端系统的生成器，增量更新静态页面。
4. Home Server将静态页面更新到Github Pages等静态网站托管服务上。

也就是说，博客基础功能部分不需要Remote Server参与。

<br>

本代码仓库中存放的是以.NET为主体的项目主要代码，其他实现将在其他仓库中，如：

- 以Cloudflare Worker为目标平台的Remote Server 
- 使用Vue等前端框架的博客前端

<br>

---

## 当前进度概览

### ✅ 已完成

#### 基础架构
- [x] Home Server 项目框架（Blazor Server + Razor Pages 混合）
- [x] Remote Server 项目框架（ASP.NET Core Minimal API，当前仍为模板代码）
- [x] Shared Core 项目框架（`SiteDbContext` 骨架，用于静态站点生成数据，尚未定义数据模型字段；Identity 相关表在 Home Core 的 `AppDbContext` 中）
- [x] 配置系统（多 JSON 配置文件分离、容器环境 `/Configurations` 自动检测）
- [x] Serilog 日志系统（支持配置文件覆盖）
- [x] Docker 相关（`.dockerignore`）

#### 数据库
- [x] EF Core 数据库基础设施（目前支持 SQLite，InfrastructureModule 结构支持多数据库扩展）
- [x] 首次数据库迁移（ASP.NET Identity 全套表：Users/Roles/Claims 等）
- [x] Infrastructure 层独立（计划支持 SQLite 和 MariaDB）

#### 身份认证
- [x] 自定义用户实体（`BocchiUserEntity`）
- [x] 自定义角色（`BocchiRoleIdentity`）
- [x] 登录页面（同时支持用户名或邮箱登录）
- [x] 注册页面（首次注册后自动禁止再注册，作为临时保护逻辑）
- [x] ASP.NET Core Identity 集成（Cookie 认证、重新验证）

#### UI 基础
- [x] 初始化向导页面（`/Setup`：检查数据库状态、一键执行迁移）
- [x] 基础导航菜单（含 Posts、Category 入口）
- [x] `StatusMessage` 通用提示组件（支持多种 Bootstrap alert 类型）
- [x] Bootstrap + Bootstrap Icons 主题

<br>

---

## 后续 Todo List

> 优先级参考：🔴 高 / 🟡 中 / 🟢 低(待定)

### 🔴 Home Server — 核心业务

#### 文章管理 (Posts)
- [ ] 定义文章实体（`PostEntity`：标题、内容原文、摘要、发布时间、状态、分类、标签等）
- [ ] 数据库迁移（添加文章相关表）
- [ ] 文章列表页面（分页、筛选、搜索）
- [ ] 文章创建 / 编辑页面（集成 Markdown 编辑器）
- [ ] 文章预览（Markdown 实时渲染）
- [ ] 文章状态管理（草稿 / 已发布 / 已撤稿）
- [ ] 文章删除

#### 分类管理 (Category)
- [ ] 定义分类实体（`CategoryEntity`）
- [ ] 数据库迁移（添加分类表）
- [ ] 分类管理 UI（增删改查，当前 `/Category` 页面仅有占位文字）
- [ ] 文章与分类的关联

#### 标签管理 (Tags)
- [ ] 定义标签实体（`TagEntity`）
- [ ] 标签管理 UI
- [ ] 文章与标签的多对多关联

#### 短文 / 微博 (Notes)
- [ ] 定义短文实体（类 Twitter/Mastodon 的短内容）
- [ ] 短文发布 / 编辑 / 删除页面
- [ ] 短文列表管理

### 🔴 Home Server — 账户与权限

- [ ] 退出登录功能
- [ ] 修改密码页面
- [ ] 个人信息编辑页面
- [ ] 全局授权中间件 / 路由保护（未登录时自动跳转登录页）

### 🟡 Home Server — Setup 向导完善

- [ ] 站点基础配置（站点名称、描述、URL、作者信息等）
- [ ] `IsSiteSettingReady` 实现真实检查逻辑（当前硬编码为 `true`）
- [ ] Setup 完成后的引导跳转

### 🟡 Home Server — Dashboard 仪表板

- [ ] 首页仪表板（当前 `/` 仅为 Hello World）
- [ ] 展示统计数据：文章总数、草稿数、分类数、评论数等
- [ ] 系统状态显示（数据库连接、最近发布等）

### 🔴 Home Server — 静态站点生成（核心流程）

- [ ] Markdown 解析器集成（如 Markdig）
- [ ] 文章预处理管线：段落拆分、自动摘要生成、多媒体资源处理
- [ ] 定义中间格式（结构化 JSON/数据模型，供前端生成器消费）
- [ ] 完善 `Bocchi.Shared.Core` 的 `SiteDbContext`（静态站点相关数据模型：已生成的页面记录、资源路径等）
- [ ] 静态站点生成器触发机制（文章保存/发布时触发）
- [ ] 增量更新支持（只重新生成变更的页面）
- [ ] 推送至静态托管（GitHub Pages 或其他平台）的集成

### 🟡 Home Server — RSS

- [ ] RSS Feed 生成（Atom/RSS 2.0）
- [ ] RSS 端点（`/rss` 或 `/feed`）

### 🔴 Remote Server — API 实现

- [ ] 清理当前模板占位代码（Todos 示例接口）
- [ ] 博客文章只读 API（对外暴露静态站点之外的动态数据）
- [ ] 评论 API（接收游客评论、存储、反垃圾）
- [ ] 短文 API（发布/列表）
- [ ] 与 Home Server 的数据同步机制（推送 or 拉取）
- [ ] Cloudflare Worker 部署适配（在另一仓库中实现）

### 🟡 评论系统

- [ ] 评论实体设计（Remote Server 侧存储）
- [ ] Home Server 评论审核/管理界面
- [ ] 评论通知

### 🟢 ActivityPub（低优先级，待定）

- [ ] ActivityPub 协议基础支持
- [ ] Actor 端点
- [ ] 收件箱/发件箱

### 🟢 博客前端（其他仓库）

> 以下为跨仓库任务，记录于此便于整体追踪

- [ ] 静态页面框架搭建（Vue 或其他前端框架）
- [ ] 博客文章静态页面模板
- [ ] 评论 Web Component（嵌入静态页，调用 Remote Server）
- [ ] 短文列表 Web Component
- [ ] RSS 链接集成
- [ ] SEO 优化（静态 HTML 可被搜索引擎抓取）