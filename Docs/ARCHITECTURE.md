# 项目架构设计

## 整体架构

Bocchi 采用 **Headless CMS + 静态站点** 架构，将传统 CMS 的服务端拆分为两部分，以降低公网部署成本并提升安全性。

```
┌─────────────────────────────────────┐
│           博客前端（静态站点）         │
│  主要托管在 GitHub Pages 等静态服务   │
│  动态部分通过 Web Component 嵌入      │
└──────────────┬──────────────────────┘
               │  HTTP / API
               ▼
┌──────────────────────────────────────┐
│         Remote Server（公网）         │
│  轻量级 API：评论、短文、动态内容      │
│  目标：可运行在 Serverless 平台       │
└──────────────┬───────────────────────┘
               │  内网 / 按需同步
               ▼
┌──────────────────────────────────────┐
│         Home Server（私有）           │
│  内容管理：文章、页面、媒体           │
│  静态站点生成 & 部署                  │
│  可部署在局域网 / 个人 PC             │
└──────────────────────────────────────┘
```

---

## Home Server

### 职责

- 文章、页面的创建、编辑与存储（内容主数据库）
- 触发静态站点生成器，增量更新前端静态页面
- 将生成的静态页面推送到 GitHub Pages 等静态托管服务
- 提供 Dashboard 供博主管理内容

### 技术栈

| 层 | 技术 |
|---|---|
| Web 框架 | ASP.NET Core 9.0（Razor Pages + Blazor Server） |
| ORM | Entity Framework Core 9.0 |
| 数据库 | SQLite（默认）/ MariaDB（可选） |
| 认证 | ASP.NET Core Identity |
| 日志 | Serilog |

### 项目分层（`Src/Home/`）

```
Bocchi.Home.WebHost         # 入口 & UI 层（Razor Pages + Blazor）
Bocchi.Home.Core            # 业务逻辑层（实体、数据库上下文、领域服务）
Bocchi.Home.Infrastructure  # 基础设施层（EF Core 迁移、数据库适配）
```

### 数据流：文章发布

```
编辑文章（Dashboard）
    │
    ▼
Home Server 解析 Markdown
    │
    ▼
预处理（摘要生成、段落拆分、多媒体处理）
    │
    ▼
触发静态站点生成器（增量更新）
    │
    ▼
推送到 GitHub Pages 等静态托管服务
```

---

## Remote Server

### 职责

- 处理动态业务：评论提交与查询、短文（Note）发布与查询
- 提供轻量 JSON API 供前端调用
- 尽可能精简，以支持 Serverless 平台部署

### 技术栈

| 层 | 技术 |
|---|---|
| Web 框架 | ASP.NET Core 9.0（Minimal API） |
| 编译方式 | AOT（Native AOT，冷启动快，适合 Serverless） |
| 认证 | JWT |
| 部署目标 | Docker / Cloudflare Workers（独立仓库） |

### 项目结构（`Src/Remote/`）

```
Bocchi.Remote.WebApi        # 主项目（Minimal API）
```

> 注意：以 Cloudflare Workers 为目标平台的 Remote Server 实现在独立仓库中维护。

---

## Shared

### 职责

提供 Home Server 和 Remote Server 之间共享的基础代码（实体定义、通用工具等）。

```
Bocchi.Shared.Core          # 共享库
```

---

## 博客前端（独立仓库）

- 以静态页面为主，可直接部署在 GitHub Pages、Vercel、Cloudflare Pages 等平台
- SEO 友好：搜索引擎可直接爬取静态页面
- 动态部分（评论列表、短文等）以 Web Component 形式嵌入，向 Remote Server 发起请求
- 技术框架：Vue（或其他前端框架，前端部分可替换）

---

## 数据存储策略

| 数据类型 | 存储位置 | 备注 |
|---|---|---|
| 文章、页面正文 | Home Server 数据库 | 主数据，需备份 |
| 媒体文件 | Home Server 本地存储 | 需备份 |
| 生成的静态页面 | GitHub Pages 等 | 可重新生成，无需单独备份 |
| 评论 | Remote Server 数据库 | 需备份 |
| 短文（Note） | Remote Server 数据库 | 需备份 |

---

## 离线降级策略

在 Remote Server 离线的情况下，静态页面仍可正常访问（用户可正常阅读文章），只有评论、短文等动态功能不可用。
