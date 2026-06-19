# xUnit v3 与 Microsoft Testing Platform v2 迁移设计

## 目标

将五个测试项目从已弃用的 `xunit` v2 元包迁移到 `xunit.v3.mtp-v2`，并让 .NET 10 SDK 的 `dotnet test` 统一使用 Microsoft Testing Platform v2。迁移同时以 Microsoft Code Coverage extension 替换不支持 MTP 模式的 Coverlet collector。

## 已确认事实

- NuGet 已将 `xunit` 2.9.3 标记为 Deprecated，并将 `xunit.v3` 列为替代包。
- 当前稳定版 `xunit.v3.mtp-v2` 为 3.2.2；当前稳定版 `Microsoft.Testing.Extensions.CodeCoverage` 为 18.8.0。
- `xunit.runner.visualstudio` 3.1.5 本身没有弃用，但仅用于保留 VSTest 兼容性；本次选择直接以 MTP v2 作为唯一测试运行路径，因此不再保留该适配器及 `Microsoft.NET.Test.Sdk`。
- 仓库测试代码未使用 `async void`、`IAsyncLifetime`、`Xunit.Abstractions` 或自定义 xUnit extensibility API；现有 `[Fact]`、`[Theory]`、fixture 与 assertion 用法不需要源代码迁移。

## 设计

### 测试框架与运行器

`Directory.Packages.props` 只保留两项测试基础设施版本：

- `xunit.v3.mtp-v2` 3.2.2：提供 xUnit v3、analyzers 与 MTP v2 集成。
- `Microsoft.Testing.Extensions.CodeCoverage` 18.8.0：提供 MTP 原生覆盖率收集。

删除不再被引用的 `xunit`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk` 与 `coverlet.collector` 中央版本。五个测试项目同步将原有四项引用替换为上述两项，避免同时维护 VSTest 与 MTP 两套运行链路。

### 项目输出形态

xUnit v3 测试项目是可独立执行的应用。所有测试项目都位于 `Tests/` 下，因此在 `Tests/Directory.Build.props` 统一设置 `<OutputType>Exe</OutputType>`，不在五个 `.csproj` 中重复配置。

### .NET SDK 测试入口

在现有 `global.json` 中保留 SDK 版本和 roll-forward 设置，并新增：

```json
"test": {
  "runner": "Microsoft.Testing.Platform"
}
```

这使 .NET 10 SDK 的 `dotnet test` 走 MTP。现有无筛选的 `dotnet test` 命令继续有效；以后需要筛选时应使用 MTP 的 `--filter-class`、`--filter-method` 等参数，而不是 VSTest 的 `--filter FullyQualifiedName...`。

### 覆盖率

覆盖率验证使用：

```powershell
dotnet test <test-project> -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

输出位于对应测试项目构建目录下的 `TestResults/coverage.cobertura.xml`。本次只验证一个代表性测试项目能够产出合法 Cobertura XML，不新增长期覆盖率阈值或报告生成工具。

## 修改范围

- `Directory.Packages.props`
- `global.json`
- `Tests/Directory.Build.props`
- 五个 `Tests/**/*.csproj`

不修改现有测试源代码，不修改用户正在调整的 `Bocchi.slnx`，也不重写里程碑文档中的历史验证记录。

## 验证标准

1. 五个测试项目均可 restore、build 并通过 MTP v2 执行全部测试。
2. 测试输出能够识别为 xUnit.net v3 / Microsoft.Testing.Platform，而非 VSTest adapter。
3. 代表性项目可以生成非空、可解析的 Cobertura XML。
4. 项目文件中不存在 `xunit` v2、`Microsoft.NET.Test.Sdk`、`xunit.runner.visualstudio` 或 `coverlet.collector` 的有效引用。
5. `git diff --check` 通过，且现有 `Bocchi.slnx` 与无关依赖升级不被改写。

## 测试策略说明

本次改动仅涉及 NuGet/MSBuild/SDK 配置，没有可合理表达为业务单元测试的新行为。采用迁移前依赖审计与迁移后全量测试作为验证，不制造一个只能证明包名变化的失败测试。
