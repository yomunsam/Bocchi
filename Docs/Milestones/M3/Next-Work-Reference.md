# M3 Closeout Reference

日期：2026-05-14

## 结论

本轮清理后，M3 之前“不 100% 满意”的问题已经收口到可接受状态：代码、测试和文档重新对齐，`Bocchi.slnx` 是唯一 solution 入口，Generator / HomeServer / Workspace 的验证链路已恢复全绿。

## 已修复

- 解决方案格式已用 `dotnet solution migrate` 从旧 solution 格式升级为 `Bocchi.slnx`，旧 solution 文件已移除。
- `Bocchi.Generator` 补回 `BuildLog` / `BuildLogLevel`，解决此前无法编译的问题。
- 构建 manifest 统一为 `output/public/.bocchi-manifest.json`，与 M3 文档约定一致。
- Pipeline 顺序调整为 `WriteManifestStage` 后执行 `ValidateOutputStage`，使 manifest 一致性校验可真实发生。
- `ValidateOutputStage` 现在会校验必需 Theme 输入、manifest 条目一致性、站点产物非空，并在 Full Build 文件系统输出中清理孤儿文件。
- Live Preview 不再写入 BuildRun；Full Build 才持久化构建记录。
- `/_bocchi/preview/data/{name}.json`、`/_bocchi/preview/media/{path}`、`robots.txt` / `sitemap.xml` / `feed.xml` / `.bocchi-manifest.json` 预览端点已打通。
- `/build/download` 现在要求存在成功 BuildRun，避免初始化空目录时下载无意义 zip。
- 构建指纹现在纳入输出选项、Theme id 和 Bocchi 版本，避免配置变化被误判为 up-to-date。
- HomeServer 测试环境隔离 Serilog，避免多个 `WebApplicationFactory` 互相污染全局 logger。
- Windows 下 LibGit2Sharp 测试清理临时目录时增加重试与只读属性清理，避免 `.git/objects` 文件句柄或属性导致测试失败。
- `BuildPageTests` 与 `PreviewEndpointTests` 已补上，覆盖 `/build`、`/build/run`、`/build/download`、preview data/media 和路径穿越拒绝。
- `.editorconfig` 与 `.gitattributes` 统一使用 LF，`dotnet format` 与 `git diff --check` 不再互相打架。
- `.gitignore` 不再用粗粒度 `BuildLog.*` 误忽略源码 `BuildLog.cs`，只忽略实际构建日志文件。

## LibGit2Sharp 使用确认

- 当前包版本：`LibGit2Sharp` 0.31.0。
- 官方 NuGet 信息：0.31.0 是当前稳定版本；包目标包含 `net8.0`，NuGet 计算兼容 `net10.0`。
- 当前使用范围：只在内容空间根执行本地 `init` / `status` / `commitAll`，不触碰 Bocchi 系统空间，也不做 remote / push / pull / 凭据。
- 当前判断：继续使用 LibGit2Sharp 是合适的。真正需要谨慎的是 Windows 测试清理和未来远程凭据设计，而不是库本身。

## 当前验证

- `dotnet build Bocchi.slnx`：通过，0 警告 0 错误。
- `dotnet test Bocchi.slnx`：通过。
  - `Bocchi.ContentModel.Tests`：4 / 4
  - `Bocchi.GeneratorContract.Tests`：4 / 4
  - `Bocchi.Workspace.Tests`：23 / 23
  - `Bocchi.Generator.Tests`：10 / 10
  - `Bocchi.HomeServer.Tests`：9 / 9
- `dotnet format Bocchi.slnx --verify-no-changes --no-restore`：通过。
- `git diff --check`：通过。
- `dotnet run --project Src/HomeServer/Bocchi.HomeServer -- build`：通过，临时工作区生成 `output/public/.bocchi-manifest.json`。

## 自检

当前 M0-M3 的既有代码我可以接受为“舒服、正确、完善”的基线：不是说没有后续功能要做，而是已有承诺已经不再靠文档硬撑，能由测试和实际代码闭环证明。
