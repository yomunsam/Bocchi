# Fluid Static Theme Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将公共 `fluid-static` renderer 与具体的 Bocchi Mono Theme 完全拆开，并让 Cozy 成为不依赖 Bocchi Mono、无需 .NET/Node.js 工具链的独立第三方 Theme demo。

**Architecture:** `Bocchi.Theme.FluidStatic` 只实现公开 Fluid Static v1 profile；`Bocchi.Theme.BocchiMono` 只嵌入和物化 `Themes/bocchi-mono`。Generator 直接依赖这两个明确组件，但二者互不引用；第三方 Theme 只通过文件 Contract 使用 Fluid Static。

**Tech Stack:** .NET 10、C#、Fluid.Core、xUnit、FluentAssertions、Liquid、原生 JavaScript、MSBuild embedded resources

---

## File structure

### New or renamed projects

- `Src/Themes/Bocchi.Theme.FluidStatic/Bocchi.Theme.FluidStatic.csproj`：公共 renderer 与通用前端 runtime。
- `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticRenderer*.cs`：输入读取、page model、内容映射、页面输出。
- `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticLiquidRenderer.cs`：Liquid parser、`html` / `t` filters、严格模板读取。
- `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticTextResolver.cs`：Common、Theme 私有文案和语言 fallback。
- `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticTemplateContract.cs`：v1 必需模板清单与检查。
- `Src/Themes/Bocchi.Theme.FluidStatic/Runtime/fluid-static-v1.js`：跨 Theme 的语言、appearance、`bocchi-time` runtime。
- `Src/Themes/Bocchi.Theme.BocchiMono/Bocchi.Theme.BocchiMono.csproj`：具体内置 Theme 资源项目。
- `Src/Themes/Bocchi.Theme.BocchiMono/BocchiMonoThemeDefinition.cs`：内置 Theme 身份与物化入口。
- `Src/Themes/Bocchi.Theme.BocchiMono/BocchiMonoThemeResources.cs`：跨平台 embedded resource 索引与读取。
- `Themes/bocchi-mono/`：由 `Themes/default-static/` 直接改名后的具体 Theme。

### Existing Bocchi files modified

- `Bocchi.slnx`：替换 Theme project，加入 BocchiMono project。
- `Src/Core/Bocchi.Generator/Bocchi.Generator.csproj`：同时引用 FluidStatic 与 BocchiMono。
- `Src/Core/Bocchi.Generator/Theme/ThemeRunner.cs`：调用 `FluidStaticRenderer`。
- `Src/Core/Bocchi.Generator/Theme/ThemeResolver.cs`：只通过 `BocchiMonoThemeDefinition` 识别内置 Theme。
- `Src/Core/Bocchi.Generator/Theme/ThemePackageService.cs`：禁止 zip 覆盖 `bocchi-mono`，校验 Theme 私有 i18n namespace。
- `Src/Core/Bocchi.Generator/Theme/ThemeManifestValidator.cs`：Resolver 与 Package 共用的 manifest 语义校验。
- `Src/Core/Bocchi.Generator/GeneratorServiceCollectionExtensions.cs`：更新 namespace。
- `Src/Core/Bocchi.Workspace/BocchiDataInitializer.cs`：新 workspace 默认 Theme id。
- `Src/HomeServer/Bocchi.HomeServer/**`：Setup、Settings、Theme 页面和服务中的默认 id。
- `Tests/Bocchi.Generator.Tests/FluidStaticRendererTests.cs`：由 renderer tests 改名并新增公开 profile 测试。
- `Tests/Bocchi.Generator.Tests/Fixtures/fluid-static-v1/templates/`：不依赖 Bocchi Mono 的完整最小 v1 模板 fixture。
- `Tests/Bocchi.Generator.Tests/BocchiMonoThemeTests.cs`：物化和跨平台资源路径测试。
- `Tests/Bocchi.Generator.Tests/GeneratorPipelineEndToEndTests.cs`：完整第三方 Fluid Static fixture 与 runtime 断言。
- `Tests/Bocchi.Generator.Tests/ThemeResolverTests.cs`：内置 Theme id/source 测试。
- `Tests/Bocchi.Generator.Tests/ThemePackageServiceTests.cs`：内置 Theme 覆盖与 i18n namespace 测试。
- `Tests/Bocchi.GeneratorContract.Tests/ThemeContractTests.cs`：示例 id/key 更新。
- `Tests/Bocchi.HomeServer.Tests/**`：默认 Theme 与 UI/service 断言更新。
- `Docs/Architecture.md`、`Themes/README.md`、`Docs/Guide_Hans/Themes/0_开发Theme的方式.md`：公开边界和作者指南。

### Cozy repository modified separately

- `D:/Dev Home/bocchi-theme-cozy/theme.json`：使用自己的 i18n namespace。
- `D:/Dev Home/bocchi-theme-cozy/templates/**/*.liquid`：只使用 Fluid Static v1 model/filter/runtime。
- `D:/Dev Home/bocchi-theme-cozy/assets/app.js`：只保留 Cozy 自有交互。
- `D:/Dev Home/bocchi-theme-cozy/README.md`：明确零 .NET/Node.js Theme authoring 依赖。

### Deliberately untouched

- `README.zh.md`：当前用户未跟踪文件，不纳入本改造。
- 所有 `**/Vibe/`：不读取、不修改。
- EF Core model/migrations：本改造不改变数据库 schema，不生成 migration。

## Task 1: Remove cross-Theme template fallback with TDD

**Files:**
- Modify: `Tests/Bocchi.Generator.Tests/DefaultStaticTemplateRendererTests.cs`
- Modify: `Tests/Bocchi.Generator.Tests/GeneratorPipelineEndToEndTests.cs`
- Modify: `Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj`
- Create: `Tests/Bocchi.Generator.Tests/Fixtures/fluid-static-v1/templates/layouts/base.liquid`
- Create: `Tests/Bocchi.Generator.Tests/Fixtures/fluid-static-v1/templates/pages/*.liquid`
- Create: `Src/Themes/Bocchi.Theme.DefaultStatic/DefaultStaticTemplateContract.cs`
- Modify: `Src/Themes/Bocchi.Theme.DefaultStatic/DefaultStaticFluidRenderer.cs`

- [ ] **Step 1: Add a complete, Theme-neutral template fixture**

Create all nine v1 templates under `Tests/Bocchi.Generator.Tests/Fixtures/fluid-static-v1/templates/`. Keep the HTML minimal, but exercise `content | html`, `site.title`, listing items, article body and runtime script slots. No fixture file may mention Bocchi Mono or `default-static`.

Copy the fixture with the test assembly:

```xml
<Content Include="Fixtures\fluid-static-v1\**\*" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Make existing focused tests copy the complete fixture into each temporary Theme root**

Add one test helper that recursively copies the fixture directory. Call it from `RenderIndexAsync`, `RenderSinglePostArticleAsync` and direct renderer tests before rendering. This makes tests explicit Theme consumers instead of relying on embedded default templates. Add a nested `DefaultStaticRendererFixture : IDisposable` with `ThemeRoot`, `InputDirectory`, `OutputDirectory` and `DefaultStaticRenderRequest Request`; its `CreateAsync` method creates the directories, copies the fixture and calls the existing minimal-input writers.

- [ ] **Step 3: Add a missing-template test that currently exposes Bocchi Mono fallback**

Copy the complete fixture, delete `templates/pages/posts.liquid`, then invoke the current renderer and expect its current exception type:

```csharp
[Fact]
public async Task RenderAsync_WhenRequiredThirdPartyTemplateIsMissing_FailsWithoutBuiltInFallback()
{
    using var fixture = await DefaultStaticRendererFixture.CreateAsync();
    File.Delete(Path.Combine(fixture.ThemeRoot, "templates", "pages", "posts.liquid"));

    var act = () => DefaultStaticTemplateRenderer.RenderAsync(fixture.Request);

    await act.Should().ThrowAsync<DefaultStaticThemeException>()
        .WithMessage("*templates/pages/posts.liquid*");
}
```

- [ ] **Step 4: Replace the partial pipeline fixture with a complete fixture expectation**

Update `CreateFluidStaticTheme` so its normal mode writes all v1 required templates. Add an explicit assertion that the generated HTML contains the third-party marker and contains neither `default-static` nor `bocchi-mono`.

- [ ] **Step 5: Run the focused tests and confirm the new behavior is red**

Run:

```powershell
dotnet test Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj --filter "FullyQualifiedName~FluidStatic|FullyQualifiedName~ThemePackageService|FullyQualifiedName~ThirdPartyFluidStatic" --disable-build-servers -v:minimal /m:1 /nr:false
```

Expected: the missing-template test fails because the current renderer silently reads the embedded default template.

- [ ] **Step 6: Implement strict required-template validation under the current names**

```csharp
internal static class DefaultStaticTemplateContract
{
    public static IReadOnlyList<string> RequiredTemplates { get; } =
    [
        "layouts/base.liquid",
        "pages/index.liquid",
        "pages/posts.liquid",
        "pages/works.liquid",
        "pages/notes.liquid",
        "pages/friends.liquid",
        "pages/article.liquid",
        "pages/standalone-page.liquid",
        "pages/404.liquid",
    ];
}
```

Validate the list at renderer startup. `ReadTemplateAsync` and `PageTemplateExistsAsync` inspect only the active Theme root; remove `DefaultStaticThemeDefinition.TryReadTemplateAsync` and all embedded template fallback calls.

- [ ] **Step 7: Run the focused tests and confirm green**

Run the same focused command. Expected: all selected tests pass, including the complete third-party fixture and missing-template failure.

- [ ] **Step 8: Commit strict Theme ownership**

```powershell
git add Src/Themes/Bocchi.Theme.DefaultStatic Tests/Bocchi.Generator.Tests
git commit -m "fix: prevent cross-theme template fallback"
```

## Task 2: Split the public renderer and Bocchi Mono materializer projects

**Files:**
- Create: `Src/Themes/Bocchi.Theme.FluidStatic/Bocchi.Theme.FluidStatic.csproj`
- Move/rename: `Src/Themes/Bocchi.Theme.DefaultStatic/DefaultStatic*.cs`
- Create: `Src/Themes/Bocchi.Theme.BocchiMono/Bocchi.Theme.BocchiMono.csproj`
- Create: `Src/Themes/Bocchi.Theme.BocchiMono/BocchiMonoThemeDefinition.cs`
- Create: `Src/Themes/Bocchi.Theme.BocchiMono/BocchiMonoThemeResources.cs`
- Modify: `Bocchi.slnx`
- Modify: `Src/Core/Bocchi.Generator/Bocchi.Generator.csproj`
- Modify: `Src/Core/Bocchi.Generator/Theme/ThemeRunner.cs`
- Modify: `Src/Core/Bocchi.Generator/Theme/ThemeResolver.cs`
- Modify: `Src/Core/Bocchi.Generator/GeneratorServiceCollectionExtensions.cs`

- [ ] **Step 1: Create the FluidStatic project without concrete Theme resources**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Bocchi.Theme.FluidStatic</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Fluid.Core" />
    <ProjectReference Include="..\..\Core\Bocchi.GeneratorContract\Bocchi.GeneratorContract.csproj" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Runtime\fluid-static-v1.js"
                      LogicalName="Bocchi.Theme.FluidStatic.Runtime.fluid-static-v1.js" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Move renderer files and perform semantic type renames**

Rename `DefaultStaticTemplateRenderer` to `FluidStaticRenderer`, `DefaultStaticFluidRenderer` to `FluidStaticLiquidRenderer`, `DefaultStaticRenderRequest` to `FluidStaticRenderRequest`, `DefaultStaticThemeText` to `FluidStaticTextResolver`, `DefaultStaticInlineTextRenderer` to `FluidStaticInlineTextRenderer`, and `DefaultStaticThemeException` to `FluidStaticException`.

Also rename `DefaultStaticTemplateContract` to `FluidStaticTemplateContract` without changing its now-tested behavior.

All moved classes remain documented in zh-CN and use namespace:

```csharp
namespace Bocchi.Theme.FluidStatic;
```

- [ ] **Step 3: Create the concrete BocchiMono resource project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Bocchi.Theme.BocchiMono</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <EmbeddedResource Include="..\..\..\Themes\default-static\**\*"
                      Exclude="..\..\..\Themes\default-static\build\**\*">
      <LogicalName>Bocchi.Theme.BocchiMono.Theme/%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Index actual manifest names instead of reconstructing separators**

`BocchiMonoThemeResources` builds a normalized path-to-resource-name map:

```csharp
private static readonly IReadOnlyDictionary<string, string> ResourceNames = Assembly
    .GetManifestResourceNames()
    .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
    .ToDictionary(
        name => name[ResourcePrefix.Length..].Replace('\\', '/'),
        name => name,
        StringComparer.Ordinal);
```

`Open` must call `GetManifestResourceStream(actualResourceName)`, never concatenate a normalized path back into a platform-specific manifest name.

- [ ] **Step 5: Update Generator references and direct call sites**

`ThemeRunner` calls:

```csharp
await FluidStaticRenderer.RenderAsync(new FluidStaticRenderRequest
{
    ThemeRoot = invocation.ThemeRoot,
    InputDirectory = invocation.InputDirectoryAbsolute,
    OutputDirectory = invocation.OutputDirectoryAbsolute,
    Manifest = invocation.Manifest,
    BaseUrl = invocation.BaseUrl,
    Environment = invocation.Environment,
}, cancellationToken);
```

`ThemeResolver` uses `BocchiMonoThemeDefinition.ThemeId` and `EnsureAsync` only; FluidStatic contains no reference to this type.

- [ ] **Step 6: Build to catch mechanical namespace/path mistakes**

Run:

```powershell
dotnet build Bocchi.slnx --disable-build-servers -v:minimal /m:1 /nr:false
```

Expected: build succeeds after all project/type reference updates; no `Bocchi.Theme.DefaultStatic` project remains in the solution.

- [ ] **Step 7: Commit the project split**

```powershell
git add Bocchi.slnx Src/Themes Src/Core/Bocchi.Generator Tests/Bocchi.Generator.Tests
git commit -m "refactor: split fluid static from bocchi mono"
```

## Task 3: Make the Fluid Static model Theme-neutral

**Files:**
- Modify: `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticLiquidRenderer.cs`
- Modify: `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticRenderer.cs`
- Modify: `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticRenderer.Model.cs`
- Modify: `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticTextResolver.cs`
- Modify: `Tests/Bocchi.Generator.Tests/FluidStaticRendererTests.cs`

- [ ] **Step 1: Add a failing arbitrary-config model test**

Render a fixture whose effective config contains `custom.cardLabel = "Cozy card"`; its fixture template reads `{{ theme.config.custom.cardLabel }}`. Expected before implementation: the output does not contain `Cozy card`.

- [ ] **Step 2: Expose Theme identity and effective config in the model**

Add this shape to every page model:

```csharp
["theme"] = new Dictionary<string, object?>(StringComparer.Ordinal)
{
    ["id"] = manifest.Id,
    ["name"] = manifest.Name,
    ["version"] = manifest.Version,
    ["config"] = ToTemplateValue(themeConfig),
},
```

Update `CreatePageModel` and its callers to receive the active `ThemeManifest` and the `theme.config` `JsonElement`; do not read these values from global state.

`ToTemplateValue` recursively converts `JsonElement` objects/arrays/scalars into dictionaries, arrays, strings, numbers, booleans, or null so Fluid can read arbitrary Theme fields.

- [ ] **Step 3: Replace concrete Theme keys with the current manifest namespace**

Use one readable helper because the suffix convention is reused throughout the profile:

```csharp
private static string ThemeTextKey(ThemeManifest manifest, string suffix)
    => $"theme.{manifest.Id}.{suffix}";
```

All home/listing/empty/page/back/colophon lookups derive from the active manifest. Common navigation, content and time strings remain `menu.*`, `content.*`, and `common.*`.

- [ ] **Step 4: Add a Theme-neutral `t` filter**

Create `TemplateOptions` per render so the filter can capture the current `FluidStaticTextResolver`:

```csharp
options.Filters.AddFilter("t", (input, _, _) =>
    new ValueTask<FluidValue>(
        new StringValue(text.Get(input.ToStringValue()))));
```

Keep the explicit `html` filter unchanged for trusted Markdown HTML. Do not expose a general raw-output escape hatch.

- [ ] **Step 5: Include all manifest Theme keys in client i18n JSON**

Replace hard-coded `theme.defaultStatic.*` entries with:

```csharp
var clientKeys = CommonClientI18nKeys
    .Concat(text.ThemeKeys)
    .Concat(EnumerateNavigationI18nKeys(navigation));
```

- [ ] **Step 6: Run renderer tests**

Run:

```powershell
dotnet test Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj --filter FullyQualifiedName~FluidStaticRendererTests --disable-build-servers -v:minimal /m:1 /nr:false
```

Expected: all FluidStatic renderer tests pass, including missing-template, arbitrary `theme.config`, `t` filter, encoding, URL and localization cases.

- [ ] **Step 7: Commit the public profile boundary**

```powershell
git add Src/Themes/Bocchi.Theme.FluidStatic Tests/Bocchi.Generator.Tests
git commit -m "feat: define fluid static v1 profile"
```

## Task 4: Validate Theme-private i18n namespaces consistently

**Files:**
- Create: `Src/Core/Bocchi.Generator/Theme/ThemeManifestValidator.cs`
- Modify: `Src/Core/Bocchi.Generator/Theme/ThemeResolver.cs`
- Modify: `Src/Core/Bocchi.Generator/Theme/ThemePackageService.cs`
- Modify: `Tests/Bocchi.Generator.Tests/ThemeResolverTests.cs`
- Modify: `Tests/Bocchi.Generator.Tests/ThemePackageServiceTests.cs`

- [ ] **Step 1: Add failing Resolver and Package tests**

Extend the existing `WriteThemeZipAsync` helper with an optional `i18nKey` argument and emit an `i18n.keys` block when supplied. Use existing `TempPackageDataRoot` and `CreateService` helpers:

```csharp
using var temp = new TempPackageDataRoot();
var zipPath = Path.Combine(temp.Root, "wrong-namespace.zip");
await WriteThemeZipAsync(
    zipPath,
    id: "cozy",
    version: "1.0.0",
    i18nKey: "theme.bocchi-mono.homeSelectedWriting");

var inspection = await CreateService(temp.Layout).InspectZipAsync(zipPath);

inspection.IsInstallable.Should().BeFalse();
inspection.Diagnostics.Should().Contain(x => x.Code == "theme-i18n-key-namespace-invalid");
```

Add the equivalent installed/dev-link assertion to `ThemeResolverTests`.

- [ ] **Step 2: Run the two tests and confirm red**

Expected: both invalid manifests are currently accepted.

- [ ] **Step 3: Implement one shared semantic validation method**

```csharp
internal static IEnumerable<ThemeDiagnostic> ValidatePrivateI18nNamespace(ThemeManifest manifest)
{
    var prefix = $"theme.{manifest.Id}.";
    foreach (var key in manifest.I18n?.Keys ?? [])
    {
        if (!key.Key.StartsWith(prefix, StringComparison.Ordinal) || key.Key.Length == prefix.Length)
        {
            yield return new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                "theme-i18n-key-namespace-invalid",
                $"Theme 私有 i18n key '{key.Key}' 必须使用 namespace '{prefix}'。");
        }
    }
}
```

- [ ] **Step 4: Apply identical validation to installed/dev-link/package paths**

Both `ThemeResolver.ValidateManifest` and `ThemePackageService.ValidateManifest` append these diagnostics. A bad namespace makes a Theme unavailable and blocks package installation.

- [ ] **Step 5: Run focused validation tests**

```powershell
dotnet test Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj --filter "FullyQualifiedName~ThemeResolverTests|FullyQualifiedName~ThemePackageServiceTests" --disable-build-servers -v:minimal /m:1 /nr:false
```

Expected: valid exact-id namespaces pass; another Theme namespace, bare prefix, and old `theme.defaultStatic.*` fail.

- [ ] **Step 6: Commit validation**

```powershell
git add Src/Core/Bocchi.Generator/Theme Tests/Bocchi.Generator.Tests
git commit -m "feat: isolate theme private i18n namespaces"
```

## Task 5: Extract the common Fluid Static browser runtime

**Files:**
- Create: `Src/Themes/Bocchi.Theme.FluidStatic/Runtime/fluid-static-v1.js`
- Create: `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticRuntimeResources.cs`
- Modify: `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticRenderer.Output.cs`
- Modify: `Src/Themes/Bocchi.Theme.FluidStatic/FluidStaticRenderer.Model.cs`
- Modify: `Themes/default-static/templates/layouts/base.liquid`
- Delete: `Themes/default-static/assets/app.js`
- Modify: `Tests/Bocchi.Generator.Tests/FluidStaticRendererTests.cs`
- Modify: `Tests/Bocchi.Generator.Tests/GeneratorPipelineEndToEndTests.cs`

- [ ] **Step 1: Add a failing runtime-output test**

Assert every Fluid Static build writes `_bocchi/fluid-static-v1.js`, exposes `/_bocchi/fluid-static-v1.js` as `runtime.scriptUrl`, and relativizes that URL correctly for nested pages.

```csharp
File.Exists(Path.Combine(output, "_bocchi", "fluid-static-v1.js")).Should().BeTrue();
indexHtml.Should().Contain("src=\"_bocchi/fluid-static-v1.js\"");
postHtml.Should().Contain("src=\"../../../_bocchi/fluid-static-v1.js\"");
```

- [ ] **Step 2: Move only protocol behavior into the shared runtime**

Start from the current Bocchi Mono `app.js`, retain language state, appearance state, navigation `data-*` hooks and `bocchi-time`, and change generic labels to Common keys. Do not move Theme-specific selectors, Lucide initialization, scroll styling, reveal animation or visual constants.

- [ ] **Step 3: Embed and write the runtime deterministically**

`FluidStaticRuntimeResources.CopyToAsync` reads the single explicit logical resource name and writes UTF-8 content to `<output>/_bocchi/fluid-static-v1.js` before pages are rendered.

- [ ] **Step 4: Expose the runtime URL in every page model**

```csharp
["runtime"] = new Dictionary<string, object?>(StringComparer.Ordinal)
{
    ["scriptUrl"] = "/_bocchi/fluid-static-v1.js",
},
```

The current bundled Theme layout references `{{ runtime.scriptUrl }}` and no longer ships a copied generic `assets/app.js`; Task 6 then moves that complete Theme root to `Themes/bocchi-mono`.

- [ ] **Step 5: Run focused and end-to-end tests**

```powershell
dotnet test Tests/Bocchi.Generator.Tests/Bocchi.Generator.Tests.csproj --filter "FullyQualifiedName~FluidStaticRendererTests|FullyQualifiedName~GeneratorPipelineEndToEndTests" --disable-build-servers -v:minimal /m:1 /nr:false
```

Expected: runtime exists once per output, nested URLs are correct, language/appearance/time markup remains present, and `/assets/app.js` is no longer required by Bocchi Mono.

- [ ] **Step 6: Commit runtime extraction**

```powershell
git add Src/Themes/Bocchi.Theme.FluidStatic Themes/default-static Tests/Bocchi.Generator.Tests
git commit -m "feat: provide fluid static browser runtime"
```

## Task 6: Rename and isolate the concrete Bocchi Mono Theme everywhere

**Files:**
- Move: `Themes/default-static/` → `Themes/bocchi-mono/`
- Delete: `Themes/bocchi-mono/build/`
- Modify: `Src/Themes/Bocchi.Theme.BocchiMono/Bocchi.Theme.BocchiMono.csproj`
- Modify: `Themes/bocchi-mono/theme.json`
- Modify: `Themes/bocchi-mono/templates/**/*.liquid`
- Modify: all default id call sites under `Src/` and `Tests/`
- Modify: `Bocchi.slnx`

- [ ] **Step 1: Rename the Theme root and remove tracked generated output**

The canonical root becomes `Themes/bocchi-mono`. `build/` is generated output and must not be embedded, committed, or materialized.

- [ ] **Step 2: Update manifest identity and private keys**

```json
{
  "id": "bocchi-mono",
  "name": "Bocchi Mono",
  "runner": {
    "kind": "fluid-static",
    "entry": "fluid"
  }
}
```

Rename every `theme.defaultStatic.*` key to `theme.bocchi-mono.*`. Remove declarations for Common keys already supplied by Fluid Static.

- [ ] **Step 3: Update templates to their own namespace and the public filter/model**

Example:

```liquid
<span data-bocchi-i18n="theme.bocchi-mono.homeSelectedWriting">
  {{ "theme.bocchi-mono.homeSelectedWriting" | t }}
</span>
```

- [ ] **Step 4: Replace application defaults without aliases**

Update workspace initializer, SiteProfile settings, Setup, Theme settings, Admin state and tests to `bocchi-mono`. Use `BocchiMonoThemeDefinition.ThemeId` where the Generator already owns the concrete built-in dependency; keep UI/service defaults as explicit `bocchi-mono` strings rather than moving a concrete default into GeneratorContract.

- [ ] **Step 5: Run all Bocchi test projects**

```powershell
dotnet test Bocchi.slnx --disable-build-servers -v:minimal /m:1 /nr:false
```

Expected: all tests pass with `bocchi-mono`; repository source outside the historical design/spec record has no active `default-static` or `theme.defaultStatic` reference.

- [ ] **Step 6: Commit the concrete Theme rename**

```powershell
git add Bocchi.slnx Src Tests Themes
git commit -m "refactor: rename default theme to bocchi mono"
```

## Task 7: Update Cozy as the independent third-party conformance demo

**Files:**
- Modify: `D:/Dev Home/bocchi-theme-cozy/theme.json`
- Modify: `D:/Dev Home/bocchi-theme-cozy/templates/layouts/base.liquid`
- Modify: `D:/Dev Home/bocchi-theme-cozy/templates/pages/*.liquid`
- Modify: `D:/Dev Home/bocchi-theme-cozy/assets/app.js`
- Modify: `D:/Dev Home/bocchi-theme-cozy/README.md`

- [ ] **Step 1: Give Cozy its own private i18n namespace**

Rename every manifest/template/JavaScript key to `theme.bocchi-theme-cozy.*`. Remove Common key declarations from the Theme manifest.

- [ ] **Step 2: Reference Fluid Static runtime and keep only Cozy behavior**

The layout loads:

```liquid
<script type="application/json" id="bocchi-i18n-data">{{ localization.textJson | html }}</script>
<script type="module" src="{{ runtime.scriptUrl }}"></script>
<script type="module" src="/assets/app.js"></script>
```

Cozy `assets/app.js` retains only topbar scroll state, Lucide initialization and reveal animation. Appearance, language, menu protocol and `bocchi-time` come from the public Fluid Static runtime.

- [ ] **Step 3: Verify the source has no concrete built-in dependency**

Run:

```powershell
rg -n "default-static|DefaultStatic|defaultStatic|bocchi-mono" "D:\Dev Home\bocchi-theme-cozy" -g "!**/.git/**" -g "!**/build/**"
```

Expected: no matches.

- [ ] **Step 4: Build Cozy through a Bocchi Dev Link or test DataRoot**

Run the Bocchi HomeServer build command with a temporary DataRoot whose `themes/dev-links.json` maps `bocchi-theme-cozy` to the external repo. Expected: build succeeds without materializing `bocchi-mono`, standard routes and `_bocchi/fluid-static-v1.js` exist, and output contains Cozy markers.

- [ ] **Step 5: Commit Cozy separately**

```powershell
git -C "D:\Dev Home\bocchi-theme-cozy" add theme.json templates assets/app.js README.md
git -C "D:\Dev Home\bocchi-theme-cozy" commit -m "refactor: target independent fluid static v1"
```

## Task 8: Update public docs and add final conformance checks

**Files:**
- Modify: `Docs/Architecture.md`
- Modify: `Themes/README.md`
- Modify: `Themes/bocchi-mono/README.md`
- Modify: `Docs/Guide_Hans/Themes/0_开发Theme的方式.md`
- Modify: `Docs/Milestones.md`
- Modify: relevant M5/M6 historical status documents only where they claim a current path/name
- Modify: `Tests/Bocchi.Generator.Tests/GeneratorPipelineEndToEndTests.cs`

- [ ] **Step 1: Document the technology-neutral Theme Contract**

State explicitly that Fluid Static is one Runner, `process` permits arbitrary toolchains, and Theme authors never reference a Bocchi .NET assembly.

- [ ] **Step 2: Document Fluid Static v1 authoring surface**

List required templates, public model roots, `html` and `t` filters, `_bocchi/fluid-static-v1.js`, private i18n namespace rules, static asset rules and strict missing-template errors.

- [ ] **Step 3: Point authors to Cozy rather than Bocchi Mono inheritance**

Describe Bocchi Mono as a bundled concrete Theme and Cozy as the independent third-party demo. Remove wording such as “Liquid overrides” that implies inheritance or cross-Theme fallback.

- [ ] **Step 4: Add a complete in-repo third-party conformance fixture**

The fixture must provide every required template and its own namespace. It must build while the test intentionally avoids `BocchiMonoThemeDefinition.EnsureAsync`.

- [ ] **Step 5: Commit docs and conformance coverage**

```powershell
git add Docs Themes/README.md Themes/bocchi-mono/README.md Tests/Bocchi.Generator.Tests
git commit -m "docs: publish fluid static v1 authoring contract"
```

## Task 9: Final verification and cleanup

**Files:**
- Modify only files required by failures found during this task.

- [ ] **Step 1: Check active naming leaks**

```powershell
rg -n "Bocchi\.Theme\.DefaultStatic|DefaultStatic|theme\.defaultStatic|default-static" Src Tests Themes README.md Docs/Architecture.md Docs/Guide_Hans -g "!**/Vibe/**" -g "!**/bin/**" -g "!**/obj/**"
```

Expected: no active code, tests, Theme source or current authoring docs use old names. Historical milestone prose may retain explicitly historical references only when useful.

- [ ] **Step 2: Verify dependency direction**

```powershell
rg -n "BocchiMono|bocchi-mono" Src/Themes/Bocchi.Theme.FluidStatic
```

Expected: no matches.

- [ ] **Step 3: Build the solution**

```powershell
dotnet build Bocchi.slnx --disable-build-servers -v:minimal /m:1 /nr:false
```

Expected: exit code 0, zero warnings unless an existing unrelated warning is explicitly recorded.

- [ ] **Step 4: Run the full test suite**

```powershell
dotnet test Bocchi.slnx --no-restore --disable-build-servers -v:minimal /m:1 /nr:false
```

Expected: all tests pass.

- [ ] **Step 5: Verify EF model stability**

Run the repository's existing `dotnet ef migrations has-pending-model-changes` command with the HomeServer startup project.

Expected: no pending model changes; no migration files were added.

- [ ] **Step 6: Verify repository hygiene**

```powershell
git diff --check
git status --short
git -C "D:\Dev Home\bocchi-theme-cozy" diff --check
git -C "D:\Dev Home\bocchi-theme-cozy" status --short
```

Expected: only intentional changes/commits remain; Bocchi's pre-existing untracked `README.zh.md` remains untouched.

- [ ] **Step 7: Record final evidence**

Report build/test counts, Cozy build output, dependency checks, EF result and both repository commit ids. Do not claim completion if any required verification is red.
