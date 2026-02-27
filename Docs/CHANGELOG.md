# 变更日志

本文件记录 Bocchi 项目的重要变更，格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

---

## [未发布]

### 新增
- 创建 `Docs/` 目录，新增 `PLAN.md`（计划与里程碑）、`ARCHITECTURE.md`（架构设计）、`CHANGELOG.md`（变更日志）
- 重写 `README.md`，内容更简洁，详细设计文档移至 `Docs/`

---

## [0.1.0] — 2025-05

### 新增
- 初始化 .NET 9.0 解决方案（`Bocchi.sln`），确定三层架构分层
- **Home Server**
  - `Bocchi.Home.WebHost`：ASP.NET Core + Blazor Server 入口项目，含配置管理、Serilog 日志
  - `Bocchi.Home.Core`：业务逻辑层，集成 ASP.NET Core Identity（`BocchiUserEntity`、`BocchiRoleIdentity`）与 EF Core `AppDbContext`
  - `Bocchi.Home.Infrastructure`：基础设施层，SQLite 初始迁移（`InitialCreate`），支持 SQLite / MariaDB 双数据库
  - 初始化向导页面（`/Setup`），可检查数据库状态并触发迁移
  - Docker 支持（`.dockerignore`）
- **Remote Server**
  - `Bocchi.Remote.WebApi`：Minimal API + AOT 编译骨架，Docker 就绪
- **Shared**
  - `Bocchi.Shared.Core`：共享库骨架，含 `SiteDbContext` 占位
