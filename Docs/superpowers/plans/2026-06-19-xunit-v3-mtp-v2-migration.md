# xUnit v3 与 Microsoft Testing Platform v2 迁移实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将全部测试项目迁移到 xUnit v3 与 Microsoft Testing Platform v2，并用 Microsoft Code Coverage extension 替代 Coverlet collector。

**Architecture:** 中央包版本锁定 `xunit.v3.mtp-v2` 与 Microsoft coverage extension；`Tests/Directory.Build.props` 统一声明 xUnit v3 所需的可执行输出；.NET 10 `global.json` 统一选择 MTP runner。五个测试项目只保留框架与覆盖率两个测试基础设施引用，不维持 VSTest 双轨。

**Tech Stack:** .NET 10、xUnit.net v3、Microsoft Testing Platform v2、Microsoft Code Coverage、MSBuild Central Package Management

---

### Task 1: 记录迁移前测试基线

**Files:**
- Inspect: `Tests/**/*.csproj`

- [ ] **Step 1: 逐项目执行当前测试链路**

```powershell
$projects = @(
  'Tests/Bocchi.ContentModel.Tests/Bocchi.ContentModel.Tests.csproj',
  'Tests/Bocchi.Workspace.Tests/Bocchi.Workspace.Tests.csproj',
  'Tests/Bocchi.GeneratorContract.Tests/Bocchi.GeneratorContract.Tests.csproj',
  'Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj',
  'Tests/Bocchi.HomeServer.Tests/Bocchi.HomeServer.Tests.csproj'
)
foreach ($project in $projects) {
  dotnet test $project --disable-build-servers -v:minimal /m:1 /nr:false
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: 五个项目全部通过；失败时先区分既有基线问题与迁移问题。

### Task 2: 切换依赖、项目形态与 SDK runner

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `global.json`
- Modify: `Tests/Directory.Build.props`
- Modify: `Tests/Bocchi.ContentModel.Tests/Bocchi.ContentModel.Tests.csproj`
- Modify: `Tests/Bocchi.Workspace.Tests/Bocchi.Workspace.Tests.csproj`
- Modify: `Tests/Bocchi.GeneratorContract.Tests/Bocchi.GeneratorContract.Tests.csproj`
- Modify: `Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj`
- Modify: `Tests/Bocchi.HomeServer.Tests/Bocchi.HomeServer.Tests.csproj`

- [ ] **Step 1: 更新中央测试包版本**

将 Testing ItemGroup 中的 `Microsoft.NET.Test.Sdk`、`xunit`、`xunit.runner.visualstudio`、`coverlet.collector` 替换为：

```xml
<PackageVersion Include="xunit.v3.mtp-v2" Version="3.2.2" />
<PackageVersion Include="Microsoft.Testing.Extensions.CodeCoverage" Version="18.8.0" />
```

- [ ] **Step 2: 统一测试项目输出形态**

在 `Tests/Directory.Build.props` 加入：

```xml
<!-- xUnit v3 测试程序集同时是可独立运行的测试应用。 -->
<OutputType>Exe</OutputType>
```

- [ ] **Step 3: 选择 MTP runner**

在 `global.json` 根对象加入：

```json
"test": {
  "runner": "Microsoft.Testing.Platform"
}
```

- [ ] **Step 4: 替换五个测试项目的基础设施引用**

删除四项旧引用及 Coverlet metadata，加入：

```xml
<PackageReference Include="xunit.v3.mtp-v2" />
<PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" />
```

- [ ] **Step 5: 审计旧引用**

Run: `rg -n -i --glob '!**/Vibe/**' --glob '!**/bin/**' --glob '!**/obj/**' 'Microsoft\.NET\.Test\.Sdk|xunit\.runner\.visualstudio|coverlet\.collector|Include="xunit"' Directory.Packages.props Tests global.json`

Expected: 无输出，`rg` exit code 为 1。

### Task 3: 恢复、编译并执行全部测试

**Files:**
- Verify: `Directory.Packages.props`
- Verify: `global.json`
- Verify: `Tests/Directory.Build.props`
- Verify: `Tests/**/*.csproj`

- [ ] **Step 1: 逐项目 restore**

```powershell
foreach ($project in $projects) {
  dotnet restore $project --disable-build-servers -v:minimal
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: 五个项目 restore 成功，无 package downgrade 或 deprecated package 警告。

- [ ] **Step 2: 逐项目 build**

```powershell
foreach ($project in $projects) {
  dotnet build $project --no-restore --disable-build-servers -v:minimal /m:1 /nr:false
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: 五个项目编译成功，0 error。

- [ ] **Step 3: 逐项目执行 MTP 测试**

```powershell
foreach ($project in $projects) {
  dotnet test $project --no-restore --disable-build-servers -v:minimal /m:1 /nr:false
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: 五个项目全部通过；输出采用 MTP test summary，不出现 VSTest Adapter banner。

### Task 4: 验证 MTP 筛选与覆盖率

**Files:**
- Verify: `Tests/Bocchi.ContentModel.Tests/Bocchi.ContentModel.Tests.csproj`

- [ ] **Step 1: 验证 MTP class filter**

Run: `dotnet test Tests/Bocchi.ContentModel.Tests/Bocchi.ContentModel.Tests.csproj --no-restore --filter-class ContentModelContractTests`

Expected: 只运行 `ContentModelContractTests`，命令成功。

- [ ] **Step 2: 生成并解析 Cobertura 覆盖率**

Run: `dotnet test Tests/Bocchi.ContentModel.Tests/Bocchi.ContentModel.Tests.csproj --no-restore -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml`

随后执行：

```powershell
$coverage = Get-ChildItem Tests/Bocchi.ContentModel.Tests -Recurse -Filter coverage.cobertura.xml |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1
if ($null -eq $coverage -or $coverage.Length -le 0) { throw 'Coverage file is missing or empty.' }
[xml](Get-Content -Raw -LiteralPath $coverage.FullName) | Out-Null
```

Expected: coverage 文件存在、非空且可以解析为 XML。

### Task 5: 最终差异与完整性验证

**Files:**
- Verify: all modified files

- [ ] **Step 1: 验证项目依赖图**

```powershell
foreach ($project in $projects) {
  dotnet list $project package
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: 每个项目直接引用 `xunit.v3.mtp-v2` 与 `Microsoft.Testing.Extensions.CodeCoverage`，无旧测试基础设施直接引用。

- [ ] **Step 2: 验证格式与改动边界**

Run: `git diff --check`、`git status --short`、`git diff -- Directory.Packages.props global.json Tests`。

Expected: diff check 成功；不改写 `Bocchi.slnx`；差异只包含设计批准的包、runner 与项目形态迁移。
