using System.IO.Compression;
using System.Text.Json;

using Bocchi.GeneratorContract;
using Bocchi.Themes.BuiltIn.Bundle;
using Bocchi.Workspace;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bocchi.Generator.Theme;

/// <summary>
/// Theme zip 包服务。它只做静态检查、解压、安装和回滚，不执行 Theme 包内任何命令。
/// </summary>
public sealed class ThemePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly BocchiDataLayout _layout;
    private readonly IOptions<ThemePackageOptions> _options;
    private readonly ILogger<ThemePackageService> _logger;

    /// <summary>构造 Theme Package 服务。</summary>
    public ThemePackageService(
        BocchiDataLayout layout,
        IOptions<ThemePackageOptions> options,
        ILogger<ThemePackageService> logger)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _layout = layout;
        _options = options;
        _logger = logger;
    }

    /// <summary>检查 Theme zip 包并解压到 staging；整个过程不会执行 Theme 代码。</summary>
    public async Task<ThemePackageInspection> InspectZipAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var diagnostics = new List<ThemeDiagnostic>();
        var fullPackagePath = Path.GetFullPath(packagePath);
        var inspectionId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture)
            + "-"
            + Guid.NewGuid().ToString("N");
        var workDirectory = Path.Combine(_layout.ThemeUploadCacheDirectory, inspectionId);
        var sourceRoot = Path.Combine(workDirectory, "staging");
        Directory.CreateDirectory(sourceRoot);

        if (!File.Exists(fullPackagePath))
        {
            diagnostics.Add(Error("theme-package-missing", $"Theme Package '{fullPackagePath}' 不存在。"));
            return CreateInspection(inspectionId, fullPackagePath, workDirectory, sourceRoot, null, null, diagnostics);
        }

        var fileInfo = new FileInfo(fullPackagePath);
        if (fileInfo.Length > _options.Value.MaxPackageBytes)
        {
            diagnostics.Add(Error("theme-package-too-large", $"Theme Package 超过最大大小 {_options.Value.MaxPackageBytes} bytes。"));
        }

        try
        {
            using var archive = ZipFile.OpenRead(fullPackagePath);
            var entries = AnalyzeEntries(archive, diagnostics);
            var sourcePrefix = ResolveSourcePrefix(entries, diagnostics) ?? string.Empty;
            if (diagnostics.Any(x => x.IsBlocking))
            {
                return CreateInspection(inspectionId, fullPackagePath, workDirectory, sourceRoot, null, null, diagnostics);
            }

            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
            {
                var relativePath = StripSourcePrefix(entry.NormalizedPath, sourcePrefix);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                await ExtractEntryAsync(entry.Entry, sourceRoot, relativePath, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(Error("theme-package-zip-invalid", $"Theme Package 不是有效 zip：{ex.Message}"));
        }
        catch (IOException ex)
        {
            diagnostics.Add(Error("theme-package-read-failed", $"Theme Package 读取失败：{ex.Message}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            diagnostics.Add(Error("theme-package-read-denied", $"Theme Package 无法访问：{ex.Message}"));
        }

        if (diagnostics.Any(x => x.IsBlocking))
        {
            return CreateInspection(inspectionId, fullPackagePath, workDirectory, sourceRoot, null, null, diagnostics);
        }

        var (manifest, runnerKind) = await LoadAndValidateManifestAsync(sourceRoot, diagnostics, cancellationToken).ConfigureAwait(false);
        return CreateInspection(inspectionId, fullPackagePath, workDirectory, sourceRoot, manifest, runnerKind, diagnostics);
    }

    /// <summary>安装或更新已通过 inspection 的 Theme Package；process runner 必须显式确认 trust。</summary>
    public Task<ThemePackageInstallResult> InstallOrUpdateAsync(
        ThemePackageInspection inspection,
        bool trustProcessRunner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        cancellationToken.ThrowIfCancellationRequested();

        if (!inspection.IsInstallable || inspection.Manifest is null)
        {
            throw new InvalidOperationException("Theme Package inspection 尚未通过，不能安装。");
        }

        if (inspection.RequiresTrust && !trustProcessRunner)
        {
            throw new InvalidOperationException("process runner Theme 必须显式确认信任后才能安装。");
        }

        var themeId = inspection.Manifest.Id.Trim();
        if (string.Equals(themeId, DefaultThemeBundle.ThemeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("bocchi-mono 是内置 Theme，不能通过 zip 覆盖。");
        }

        if (!Directory.Exists(inspection.SourceRoot))
        {
            throw new InvalidOperationException("Theme Package staging 目录不存在，不能安装。");
        }

        Directory.CreateDirectory(_layout.ThemesDirectory);
        var targetRoot = Path.Combine(_layout.ThemesDirectory, themeId);
        var wasUpdate = Directory.Exists(targetRoot);
        string? backupRoot = null;
        try
        {
            if (wasUpdate)
            {
                Directory.CreateDirectory(Path.Combine(_layout.ThemeBackupsDirectory, themeId));
                backupRoot = Path.Combine(
                    _layout.ThemeBackupsDirectory,
                    themeId,
                    DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture));
                Directory.Move(targetRoot, backupRoot);
            }

            Directory.Move(inspection.SourceRoot, targetRoot);
        }
        catch
        {
            TryRollback(targetRoot, backupRoot);
            throw;
        }

        _logger.LogInformation(
            "Theme package installed. ThemeId={ThemeId} TargetRoot={TargetRoot} WasUpdate={WasUpdate}",
            themeId,
            targetRoot,
            wasUpdate);
        return Task.FromResult(new ThemePackageInstallResult
        {
            ThemeId = themeId,
            TargetRoot = targetRoot,
            WasUpdate = wasUpdate,
            BackupRoot = backupRoot,
        });
    }

    private IReadOnlyList<ZipEntryInspection> AnalyzeEntries(ZipArchive archive, List<ThemeDiagnostic> diagnostics)
    {
        var entries = new List<ZipEntryInspection>();
        var warningCodes = new HashSet<string>(StringComparer.Ordinal);
        var fileCount = 0;
        foreach (var entry in archive.Entries)
        {
            var normalizedPath = NormalizeZipPath(entry.FullName, diagnostics);
            if (normalizedPath is null)
            {
                continue;
            }

            var isDirectory = normalizedPath[^1] == '/';
            if (!isDirectory)
            {
                fileCount++;
                if (entry.Length > _options.Value.MaxSingleFileBytes)
                {
                    diagnostics.Add(Error("theme-package-entry-too-large", $"Theme Package 条目 '{normalizedPath}' 超过单文件大小限制。"));
                }
            }

            AddSuspiciousPathWarnings(normalizedPath, warningCodes, diagnostics);
            entries.Add(new ZipEntryInspection(entry, normalizedPath, isDirectory));
        }

        if (fileCount == 0)
        {
            diagnostics.Add(Error("theme-package-empty", "Theme Package 不包含任何文件。"));
        }

        if (fileCount > _options.Value.MaxFileCount)
        {
            diagnostics.Add(Error("theme-package-too-many-files", $"Theme Package 文件数量超过限制 {_options.Value.MaxFileCount}。"));
        }

        return entries;
    }

    private static string? ResolveSourcePrefix(IReadOnlyList<ZipEntryInspection> entries, List<ThemeDiagnostic> diagnostics)
    {
        var filePaths = entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => entry.NormalizedPath)
            .ToList();
        if (filePaths.Contains("theme.json", StringComparer.Ordinal))
        {
            return string.Empty;
        }

        var topLevel = filePaths
            .Select(path => path.Split('/', 2)[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (topLevel.Count == 1 && filePaths.Contains(topLevel[0] + "/theme.json", StringComparer.Ordinal))
        {
            return topLevel[0] + "/";
        }

        diagnostics.Add(Error("theme-package-root-invalid", "Theme Package 根目录必须直接包含 theme.json，或只有一个顶层目录且该目录包含 theme.json。"));
        return string.Empty;
    }

    private static async Task<(ThemeManifest? Manifest, string? RunnerKind)> LoadAndValidateManifestAsync(
        string sourceRoot,
        List<ThemeDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        ThemeManifest? manifest = null;
        try
        {
            var loaded = await ThemeManifestLoader.TryLoadFromRootAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
            if (loaded is null)
            {
                diagnostics.Add(Error("theme-manifest-missing", "Theme Package 缺少 theme.json。"));
                return (null, null);
            }

            manifest = loaded.Value.Manifest;
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Error("theme-manifest-json-invalid", $"Theme Package 的 theme.json 无法解析：{ex.Message}"));
            return (null, null);
        }

        ValidateManifest(manifest, sourceRoot, diagnostics);
        await ValidateConfigSchemaAsync(sourceRoot, diagnostics, cancellationToken).ConfigureAwait(false);
        return (manifest, ResolveRunnerKind(manifest));
    }

    private static void ValidateManifest(ThemeManifest manifest, string sourceRoot, List<ThemeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || !ThemeResolver.IsValidThemeId(manifest.Id.Trim()))
        {
            diagnostics.Add(Error("theme-manifest-id-invalid", "theme.json.id 必须是合法的 Theme id。"));
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            diagnostics.Add(Error("theme-manifest-name-empty", "theme.json.name 不能为空。"));
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            diagnostics.Add(Error("theme-manifest-version-empty", "theme.json.version 不能为空。"));
        }

        if (!string.Equals(manifest.ContractVersion, ThemeContractVersion.Current, StringComparison.Ordinal))
        {
            diagnostics.Add(Error("theme-contract-unsupported", $"Theme Contract '{manifest.ContractVersion}' 不受当前 Bocchi 支持。"));
        }

        if (manifest.Runner is null)
        {
            if (manifest.Build is null || string.IsNullOrWhiteSpace(manifest.Build.Command))
            {
                diagnostics.Add(Error("theme-runner-missing", "theme.json 必须声明 runner 或旧版 build.command。"));
            }
        }
        else
        {
            var kind = manifest.Runner.Kind.Trim();
            if (!string.Equals(kind, "fluid-static", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(kind, "process", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(Error("theme-runner-unsupported", $"runner.kind '{manifest.Runner.Kind}' 尚未由当前 Generator 支持。"));
                }
                else
                {
                    var command = string.IsNullOrWhiteSpace(manifest.Runner.Command)
                        ? manifest.Build?.Command
                        : manifest.Runner.Command;
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        diagnostics.Add(Error("theme-process-command-missing", "process runner 必须声明 command。"));
                    }
                }
            }

        }

        diagnostics.AddRange(ThemeManifestValidator.ValidatePrivateI18nNamespace(manifest));
        diagnostics.AddRange(ThemeStaticAssetManifestValidator.Validate(manifest, sourceRoot));
    }

    private static async Task ValidateConfigSchemaAsync(
        string sourceRoot,
        List<ThemeDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(sourceRoot, "config-schema.json");
        if (!File.Exists(schemaPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(schemaPath);
            _ = await JsonSerializer.DeserializeAsync<ThemeConfigSchema>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Error("theme-config-schema-invalid", $"config-schema.json 无法解析：{ex.Message}"));
        }
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string stagingRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(Path.Combine(stagingRoot, relativePath));
        var stagingRootWithSeparator = stagingRoot.EndsWith(Path.DirectorySeparatorChar)
            ? stagingRoot
            : stagingRoot + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(stagingRootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Theme Package 解压目标越界。");
        }

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var source = entry.Open();
        await using var target = new FileStream(
            destination,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private static string? NormalizeZipPath(string fullName, List<ThemeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            diagnostics.Add(Error("theme-package-entry-empty", "Theme Package 包含空路径条目。"));
            return null;
        }

        var normalized = fullName.Replace('\\', '/');
        if (normalized[0] == '/' ||
            (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':'))
        {
            diagnostics.Add(Error("theme-package-entry-absolute", $"Theme Package 条目 '{fullName}' 不能是绝对路径。"));
            return null;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            diagnostics.Add(Error("theme-package-entry-empty", "Theme Package 包含空路径条目。"));
            return null;
        }

        if (segments.Any(segment => segment is "." or ".."))
        {
            diagnostics.Add(Error("theme-package-entry-traversal", $"Theme Package 条目 '{fullName}' 不能包含目录穿越片段。"));
            return null;
        }

        return normalized[^1] == '/'
            ? string.Join('/', segments) + "/"
            : string.Join('/', segments);
    }

    private static string StripSourcePrefix(string normalizedPath, string sourcePrefix)
        => string.IsNullOrEmpty(sourcePrefix)
            ? normalizedPath
            : normalizedPath[sourcePrefix.Length..];

    private static void AddSuspiciousPathWarnings(
        string normalizedPath,
        HashSet<string> warningCodes,
        List<ThemeDiagnostic> diagnostics)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        AddWarningIfSegmentExists(segments, ".git", "theme-package-contains-git", "Theme Package 包含 .git 目录，安装时会原样保留。", warningCodes, diagnostics);
        AddWarningIfSegmentExists(segments, "node_modules", "theme-package-contains-node-modules", "Theme Package 包含 node_modules，建议不要把依赖目录打进 Theme 包。", warningCodes, diagnostics);
        AddWarningIfSegmentExists(segments, "build", "theme-package-contains-build-output", "Theme Package 包含 build 目录，可能是旧构建产物。", warningCodes, diagnostics);
        AddWarningIfSegmentExists(segments, "dist", "theme-package-contains-dist-output", "Theme Package 包含 dist 目录，可能是旧构建产物。", warningCodes, diagnostics);
        AddWarningIfSegmentExists(segments, "__MACOSX", "theme-package-contains-system-files", "Theme Package 包含系统隐藏文件。", warningCodes, diagnostics);
        AddWarningIfSegmentExists(segments, ".DS_Store", "theme-package-contains-system-files", "Theme Package 包含系统隐藏文件。", warningCodes, diagnostics);
    }

    private static void AddWarningIfSegmentExists(
        IReadOnlyList<string> segments,
        string segment,
        string code,
        string message,
        HashSet<string> warningCodes,
        List<ThemeDiagnostic> diagnostics)
    {
        if (!segments.Contains(segment, StringComparer.Ordinal))
        {
            return;
        }

        if (warningCodes.Add(code))
        {
            diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, code, message));
        }
    }

    private static string? ResolveRunnerKind(ThemeManifest? manifest)
    {
        if (manifest is null)
        {
            return null;
        }

        if (manifest.Runner is null)
        {
            return string.IsNullOrWhiteSpace(manifest.Build?.Command) ? null : "process";
        }

        return manifest.Runner.Kind.Trim();
    }

    private static ThemePackageInspection CreateInspection(
        string inspectionId,
        string packagePath,
        string workDirectory,
        string sourceRoot,
        ThemeManifest? manifest,
        string? runnerKind,
        IReadOnlyList<ThemeDiagnostic> diagnostics)
        => new()
        {
            InspectionId = inspectionId,
            PackagePath = packagePath,
            WorkDirectory = workDirectory,
            SourceRoot = sourceRoot,
            Manifest = manifest,
            RunnerKind = runnerKind,
            Diagnostics = diagnostics,
        };

    private static void TryRollback(string targetRoot, string? backupRoot)
    {
        if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot) || Directory.Exists(targetRoot))
        {
            return;
        }

        Directory.Move(backupRoot, targetRoot);
    }

    private static ThemeDiagnostic Error(string code, string message)
        => new(ThemeDiagnosticSeverity.Error, code, message);

    /// <summary>zip 条目的归一化视图。</summary>
    private sealed record ZipEntryInspection(ZipArchiveEntry Entry, string NormalizedPath, bool IsDirectory);
}
