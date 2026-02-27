# 项目计划与里程碑

## 项目概述

Bocchi 是一套个人博客系统，采用 Headless CMS 架构，由三个主要部分组成：
- **Home Server**：私有内容管理后台
- **Remote Server**：公网轻量级 API 服务
- **博客前端**：静态页面为主的前端（独立仓库）

---

## 里程碑

### Milestone 1：基础框架搭建 ✅（已完成）

- [x] 确定整体架构（Home Server + Remote Server + 前端分离）
- [x] 搭建 .NET 9.0 解决方案结构（分层架构）
- [x] Home Server - 项目分层（WebHost / Core / Infrastructure）
- [x] Home Server - EF Core + ASP.NET Identity 集成
- [x] Home Server - SQLite 数据库迁移（InitialCreate）
- [x] Home Server - Serilog 日志集成
- [x] Home Server - 配置文件管理（JSON + 环境变量 + UserSecrets）
- [x] Home Server - 初始化向导页面（Setup/Index）
- [x] Home Server - Blazor Server 渲染模式集成
- [x] Remote Server - 项目骨架搭建（AOT 编译支持）
- [x] Remote Server - Docker 支持
- [x] Shared.Core - 共享库骨架

---

### Milestone 2：Home Server 核心功能 🚧（进行中）

- [ ] 用户认证与管理
  - [ ] 注册 / 登录 / 登出流程
  - [ ] 初次使用时的管理员账号创建
- [ ] 博客文章管理
  - [ ] 文章实体设计（标题、正文、摘要、标签、分类、发布时间等）
  - [ ] Markdown 编辑器集成
  - [ ] 文章 CRUD（新建、编辑、删除、发布/取消发布）
  - [ ] 文章预处理（段落拆分、自动摘要、多媒体处理）
- [ ] 页面管理（About、自定义页面等）
- [ ] Dashboard 主界面 UI

---

### Milestone 3：静态页面生成与部署 ⬜（未开始）

- [ ] 静态站点生成器集成（在 Home Server 中触发）
- [ ] 增量更新静态页面
- [ ] 自动部署到 GitHub Pages 或其他静态托管服务

---

### Milestone 4：Remote Server 核心功能 ⬜（未开始）

- [ ] 评论系统 API
- [ ] 短文（Note/Status）API（类 Twitter/Mastodon）
- [ ] JWT 认证集成
- [ ] 适配 Serverless 平台部署（Cloudflare Workers 等）

---

### Milestone 5：RSS 与 ActivityPub ⬜（未开始，优先级较低）

- [ ] RSS Feed 生成
- [ ] ActivityPub 协议支持（待定）

---

### Milestone 6：博客前端 ⬜（独立仓库，未开始）

- [ ] 静态页面框架搭建（Vue 或其他前端框架）
- [ ] 文章列表与详情页
- [ ] 评论、短文等动态组件（以 Web Component 形式嵌入）
- [ ] SEO 优化（静态页面直接被搜索引擎爬取）

---

## 当前重点

当前阶段优先推进 **Milestone 2（Home Server 核心功能）**，具体从用户认证和文章管理开始。
