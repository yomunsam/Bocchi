# Bocchi

个人自用的 个人主页/Blog系统。Since 2026

## Quickstart

> 需要 .NET 10 SDK（仓库通过 `global.json` 锁定，使用 `rollForward: latestFeature`）。

```bash
# 还原依赖
dotnet restore Bocchi.sln

# 构建（已开启 TreatWarningsAsErrors，警告即失败）
dotnet build Bocchi.sln

# 跑测试（含 Home Server 集成测试）
dotnet test Bocchi.sln

# 本地启动 Home Server，默认只监听回环地址 http://127.0.0.1:5081
dotnet run --project Src/HomeServer/Bocchi.HomeServer
```

健康检查：`curl http://127.0.0.1:5081/healthz` 应返回 `Healthy`。

工作区面板：浏览器打开 `http://127.0.0.1:5081/workspace` 可看到当前内容空间路径、Git 状态以及一键扫描结果。

构建面板：浏览器打开 `http://127.0.0.1:5081/build` 可触发一次 Full Build 并查看 fingerprint、阶段日志、产物列表与历史；点击"下载 zip"即得 `output/public/` 的完整静态目录打包文件。也可以从命令行直接构建：

```bash
# 单次 Full Build（构建完成后退出，不启动 Web）
dotnet run --project Src/HomeServer/Bocchi.HomeServer -- build [--theme=<id>] [--include-drafts] [--env=<name>]
```

构建后的两个关键目录（均位于 Bocchi 系统空间，不会污染内容空间）：

- `<workspace>/.bocchi/input/`：Theme 输入数据 JSON（`site.json` / `posts.json` / `pages.json` / `works.json` / `notes.json` / `friends.json` / `photos.json` / `navigation.json` / `theme-config.json` / `build-context.json`）
- `<workspace>/output/public/`：可部署到任意静态托管的站点目录，含 `robots.txt` / `sitemap.xml` / `feed.xml` / `media/...` / `build-manifest.json`，M5 起会再加入 Theme 渲染输出

实时预览端点：`GET /_bocchi/preview/<artifact 相对路径>`（仅支持 `.json` / `.xml` / `.txt`）会触发一次 Live 模式构建并将命中 artifact 流式吐出，供编辑器实时预览使用。

### 内容空间（Content Space）

Bocchi 把"工作区"严格切分为两部分：

- **内容空间**（默认 `<workspace>/content/`）：纯创作资产（Blog、作品集、短文、友链、site.yaml），独立可携、可作为独立 Git 仓库迁移。任何时候都能整体打包带走，不依赖 Bocchi 的代码。
- **Bocchi 系统空间**（`<workspace>/.bocchi/`、`themes/`、`output/`）：Bocchi 程序的状态库（SQLite）、日志、缓存、Theme 输入与构建产物，与 Bocchi 同寿。

通过 `appsettings.json` 中的 `Bocchi:WorkspaceRoot`（或环境变量 `Bocchi__WorkspaceRoot`）指向你已有的工作区目录；留空时回退到 `<ContentRoot>/workspace/`。首次启动会自动创建必需的目录结构（包括 `content/README.md` 与 `content/.gitignore`），然后在 `/workspace` 页面手动触发一次扫描即可。

更深入的文档：

- `Docs/Architecture.md`：目标架构与模块边界
- `Docs/Milestones.md`：里程碑总览
- `Docs/Milestones/M1/M1.md`：M1 阶段详细规划与验证记录
- `Docs/Milestones/M2/M2.md`：M2 阶段详细规划与验证记录
- `Docs/Milestones/M3/M3.md`：M3 阶段详细规划与验证记录

## 简介

本项目可以看作三个部分：

1. Home Server：内容管理系统 + Dashboard
    - 基于 .NET 10 技术栈
    - 部署在 NAS、个人服务器等环境中
    - 不作为对外直接提供访问的服务器，甚至不包含Api，但包含管理界面。
    - 以相对粗狂的方式管理Blog内容（主要基于文件和Markdown，少量的Sqlite数据库）
2. Page Frontend：个人主页实际对外展示的页面
    - 主观上希望它是一个静态站点，实际可能会包含少量动态内容
    - 含有RSS、Sitemap等功能
    - 由Home Server负责生成和增量更新，并部署到CDN或静态站点托管服务（如GitHub Pages、Vercel、Cloudflare Pages等，前期主要目标是输出到本地目录，以及Cloudflare Pages）
    - 希望是模块化的，含有Theme概念，下文讨论细节。
3. Cloud Server：把少量必须依赖Server的功能放在这里
    - 先预留这么个东西，以后有什么功能需要Server了再说

## 主页功能

从内容管理角度，目前的个人主要包括这么几个功能（通用块面约定）
- 文章管理（基于Markdown文件，支持分类、标签、草稿等功能）
- 基于Markdown的页面管理（如 About 页面）
- 我的作品（需要列表和详情页）
- 短文（类似于Twitter的功能，支持图片、视频等媒体内容）
- 友链管理
- 站点设置（如站点标题、描述、社交媒体链接等）
- 照片墙（预留）

## 模块化前端

总体上的想法是，我们完全不干涉前端用什么技术栈，含有什么内容，只要它能满足一些基本的接口规范就行了。前端的内容和结构完全由用户定义，Home Server只负责提供数据和管理界面。

我们需要做一个默认的基础前端，我倾向于SvelteKit，为后续的评论、短文等动态内容预留前端开发能力。

然后我估计要有一个类似模板引擎的东西，我们的Home Server使用dotnet技术栈的话，希望找一个贴近技术栈的库。（Razor估计不行，毕竟我们不要一个dll文件，而是要一堆静态文件，除非dotnet 9/10引入了什么我不知道的新东西）

### 自定义配置项

我觉得需要这么一个功能，比如某一套Theme可能会有一些自定义的配置项（比如说是否显示某个模块，或者某个模块的标题是什么），我们需要在Home Server里提供一个界面让用户可以设置这些配置项，并且把这些配置项传递给前端。

这些东西我倾向于在服务器那边，每套Theme有个单独的配置文件，比如json或者sqlite，用来记录配置项。

可能还需要一个描述文件，比如`config-schema.json`，用来描述这个Theme有哪些配置项，每个配置项是什么类型的，默认值是什么等等，以及在设置界面的分类页签、展示方式等，这样Home Server就可以根据这个描述文件自动生成配置界面。

## 其他功能

- RSS生成
- Sitemap生成
- 搜索功能（实现方式待定）