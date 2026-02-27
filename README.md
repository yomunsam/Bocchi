# Bocchi

一套以 .NET 为核心的个人博客系统。Since 2025

## 简介

Bocchi 采用 **Headless CMS + 静态站点** 架构，将内容管理与公网服务分离：

- **Home Server**：私有内容管理后台，可运行在局域网或个人 PC，含 Dashboard
- **Remote Server**：部署在公网的轻量级 API（评论、短文等），支持 Serverless 平台
- **博客前端**：以静态页面为主，SEO 友好，可托管在 GitHub Pages 等平台（独立仓库）

## 技术栈

| 组件 | 技术 |
|---|---|
| Home Server | .NET 9 · ASP.NET Core · Blazor Server · EF Core · SQLite/MariaDB |
| Remote Server | .NET 9 · Minimal API · AOT 编译 · Docker |
| 博客前端 | Vue（独立仓库） |

## 仓库结构

```
Src/
├── Home/
│   ├── Bocchi.Home.WebHost       # Web 入口（Razor Pages + Blazor）
│   ├── Core/Bocchi.Home.Core     # 业务逻辑
│   └── Infrastructure/...        # 数据库迁移
├── Remote/
│   └── Bocchi.Remote.WebApi      # 公网 API
└── Shared/
    └── Bocchi.Shared.Core        # 共享库
```

## 文档

- [计划与里程碑](Docs/PLAN.md)
- [架构设计](Docs/ARCHITECTURE.md)
- [变更日志](Docs/CHANGELOG.md)