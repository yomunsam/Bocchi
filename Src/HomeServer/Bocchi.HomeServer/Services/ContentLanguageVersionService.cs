using System.Globalization;

using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 编辑器语言版本服务。它负责把 state store 中的 localization group 映射成编辑器可用的版本视图，
/// 并创建新的 sibling Markdown variant 文件。
/// </summary>
public sealed class ContentLanguageVersionService
{
    /// <summary>语言代码会进入文件名，因此这里排除 scanner 当前不支持的路径字符与点号。</summary>
    private static readonly char[] InvalidLanguageFileNameChars =
    [
        '/',
        '\\',
        '.',
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
    ];

    private readonly IContentStateStore _store;
    private readonly LocalizationSettingsService _localization;
    private readonly ContentEditingService _editor;
    private readonly ContentScanner _scanner;

    /// <summary>构造编辑器语言版本服务。</summary>
    public ContentLanguageVersionService(
        IContentStateStore store,
        LocalizationSettingsService localization,
        ContentEditingService editor,
        ContentScanner scanner)
    {
        _store = store;
        _localization = localization;
        _editor = editor;
        _scanner = scanner;
    }

    /// <summary>读取当前文件所在 localization group 的版本上下文；state 过旧时会先刷新一次扫描投影。</summary>
    public async Task<ContentLanguageVersionsView?> GetAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(relativePath, refreshWhenMissing: true, cancellationToken).ConfigureAwait(false);
        return context is null ? null : CreateView(context);
    }

    /// <summary>创建一个新的语言版本，写入文件后刷新扫描投影，并返回新文件路径。</summary>
    public async Task<ContentLanguageVariantCreateResult> CreateAsync(
        CreateContentLanguageVariantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = await LoadContextAsync(request.CurrentRelativePath, refreshWhenMissing: true, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("当前内容尚未进入内容索引，请先保存并刷新。");

        var targetLanguage = ResolveEnabledTargetLanguage(context.Settings, request.TargetLanguage);
        EnsureLanguageCanBeWrittenToVariantFile(targetLanguage.Code);
        if (context.Variants.Any(x => SameCode(x.Language, targetLanguage.Code)))
        {
            throw new InvalidOperationException("这个 localization group 已经存在目标语言版本。");
        }

        var sourceVariant = request.IsTranslation
            ? ResolveTranslationSource(context, request.SourceContentId)
            : null;
        var groupId = context.Current.LocalizationGroup
            ?? throw new InvalidOperationException("当前内容缺少 localization group，无法创建语言版本。");
        var currentFile = await _editor.ReadAsync(context.Current.RelativePath, cancellationToken).ConfigureAwait(false);
        var targetRelativePath = CreateTargetRelativePath(context.Current.RelativePath, targetLanguage.Code);
        var yaml = BuildVariantYaml(currentFile.Yaml, targetLanguage.Code, groupId, sourceVariant);
        var markdown = request.CopyCurrentContent ? currentFile.Markdown : string.Empty;

        var created = await _editor.CreateLanguageVariantAsync(
            targetRelativePath,
            yaml,
            markdown,
            cancellationToken).ConfigureAwait(false);
        await _scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        return new ContentLanguageVariantCreateResult(created.RelativePath);
    }

    private async Task<LanguageVersionContext?> LoadContextAsync(
        string relativePath,
        bool refreshWhenMissing,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        var summaries = await _store.ListContentSummariesAsync(null, cancellationToken).ConfigureAwait(false);
        var current = FindCurrentSummary(summaries, normalizedPath);
        if (current is null && refreshWhenMissing)
        {
            await _scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
            summaries = await _store.ListContentSummariesAsync(null, cancellationToken).ConfigureAwait(false);
            current = FindCurrentSummary(summaries, normalizedPath);
        }

        if (current is null || current.Kind is not (ContentKind.Post or ContentKind.Page or ContentKind.Work))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(current.LocalizationGroup))
        {
            return null;
        }

        var variants = summaries
            .Where(x => x.Kind == current.Kind &&
                string.Equals(x.LocalizationGroup, current.LocalizationGroup, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Language, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var settings = await _localization.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LanguageVersionContext(current, variants, settings);
    }

    private static ContentSummary? FindCurrentSummary(IEnumerable<ContentSummary> summaries, string relativePath)
        => summaries.FirstOrDefault(x => string.Equals(
            NormalizeRelativePath(x.RelativePath),
            relativePath,
            StringComparison.OrdinalIgnoreCase));

    private static ContentLanguageVersionsView CreateView(LanguageVersionContext context)
    {
        var languageRecords = CreateLanguageRecords(context);
        var primaryLanguage = context.Settings.PrimaryLanguage.Code;
        var variants = context.Variants
            .OrderBy(x => SameCode(x.Language, primaryLanguage) ? 0 : 1)
            .ThenBy(x => x.Language, StringComparer.OrdinalIgnoreCase)
            .Select(x => CreateVariantView(x, context.Current, languageRecords))
            .ToArray();
        var current = variants.First(x => string.Equals(
            NormalizeRelativePath(x.RelativePath),
            NormalizeRelativePath(context.Current.RelativePath),
            StringComparison.OrdinalIgnoreCase));
        var existingLanguages = context.Variants
            .Select(x => x.Language)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = context.Settings.EnabledLanguages
            .Where(x => !existingLanguages.Contains(x.Code))
            .OrderBy(x => SameCode(x.Code, primaryLanguage) ? 0 : 1)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var source = ResolveSourceVariant(current, variants);
        return new ContentLanguageVersionsView(current, variants, available, source, primaryLanguage);
    }

    private static Dictionary<string, LanguageRecord> CreateLanguageRecords(LanguageVersionContext context)
    {
        var records = context.Settings.EnabledLanguages
            .Concat(context.Settings.CustomLanguages)
            .DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var language in context.Variants.SelectMany(x => new[] { x.Language, x.SourceLanguage }))
        {
            if (string.IsNullOrWhiteSpace(language) || records.ContainsKey(language))
            {
                continue;
            }

            records[language] = new LanguageRecord
            {
                Code = language,
                NativeName = language,
                EnglishName = language,
            };
        }

        return records;
    }

    private static ContentLanguageVariantView CreateVariantView(
        ContentSummary summary,
        ContentSummary current,
        Dictionary<string, LanguageRecord> languageRecords)
    {
        var language = string.IsNullOrWhiteSpace(summary.Language) ? LocalizationSettingsService.DefaultPrimaryLanguage : summary.Language;
        var languageRecord = languageRecords.TryGetValue(language, out var record)
            ? record
            : new LanguageRecord { Code = language, NativeName = language, EnglishName = language };
        return new ContentLanguageVariantView(
            summary.ContentId,
            summary.Title,
            summary.RelativePath,
            languageRecord.Code,
            FormatLanguageDisplayName(languageRecord),
            string.Equals(NormalizeRelativePath(summary.RelativePath), NormalizeRelativePath(current.RelativePath), StringComparison.OrdinalIgnoreCase),
            summary.IsTranslation,
            summary.SourceLanguage,
            summary.SourceContentId);
    }

    private static ContentLanguageVariantView? ResolveSourceVariant(
        ContentLanguageVariantView current,
        IReadOnlyList<ContentLanguageVariantView> variants)
    {
        if (!current.IsTranslation)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(current.SourceContentId))
        {
            return variants.FirstOrDefault(x => string.Equals(x.ContentId, current.SourceContentId, StringComparison.OrdinalIgnoreCase));
        }

        return string.IsNullOrWhiteSpace(current.SourceLanguageCode)
            ? null
            : variants.FirstOrDefault(x => SameCode(x.LanguageCode, current.SourceLanguageCode));
    }

    private static LanguageRecord ResolveEnabledTargetLanguage(LocalizationSettingsView settings, string targetLanguage)
    {
        var normalized = targetLanguage.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("请选择目标语言。");
        }

        return settings.EnabledLanguages.FirstOrDefault(x => SameCode(x.Code, normalized))
            ?? throw new InvalidOperationException("目标语言不在 Site enabled languages 中。");
    }

    private static ContentSummary ResolveTranslationSource(LanguageVersionContext context, string? sourceContentId)
    {
        if (string.IsNullOrWhiteSpace(sourceContentId))
        {
            return context.Current;
        }

        return context.Variants.FirstOrDefault(x => string.Equals(x.ContentId, sourceContentId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("找不到指定的翻译来源版本。");
    }

    private static string BuildVariantYaml(
        string yaml,
        string targetLanguage,
        string groupId,
        ContentSummary? sourceVariant)
    {
        var root = ParseYamlRoot(yaml);
        SetScalar(root, "language", targetLanguage);
        SetScalar(root, "status", "draft");

        var localization = GetOrCreateMapping(root, "localization");
        SetScalar(localization, "group", groupId);
        if (sourceVariant is null)
        {
            localization.Children.Remove(new YamlScalarNode("translationOf"));
        }
        else
        {
            var translationOf = new YamlMappingNode();
            SetScalar(translationOf, "language", sourceVariant.Language);
            SetScalar(translationOf, "contentId", sourceVariant.ContentId);
            localization.Children[new YamlScalarNode("translationOf")] = translationOf;
        }

        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        return writer.ToString().Trim();
    }

    private static YamlMappingNode ParseYamlRoot(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new YamlMappingNode();
        }

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            return stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode mapping
                ? mapping
                : new YamlMappingNode();
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException("Frontmatter YAML 无法解析，不能创建语言版本。", ex);
        }
    }

    private static YamlMappingNode GetOrCreateMapping(YamlMappingNode root, string key)
    {
        var scalarKey = new YamlScalarNode(key);
        if (root.Children.TryGetValue(scalarKey, out var node) && node is YamlMappingNode mapping)
        {
            return mapping;
        }

        mapping = new YamlMappingNode();
        root.Children[scalarKey] = mapping;
        return mapping;
    }

    private static void SetScalar(YamlMappingNode root, string key, string? value)
        => root.Children[new YamlScalarNode(key)] = new YamlScalarNode(value?.Trim() ?? string.Empty);

    private static string FormatLanguageDisplayName(LanguageRecord language)
        => string.Equals(language.NativeName, language.EnglishName, StringComparison.Ordinal)
            ? language.NativeName
            : $"{language.NativeName} / {language.EnglishName}";

    private static string CreateTargetRelativePath(string currentRelativePath, string targetLanguage)
    {
        var normalized = NormalizeRelativePath(currentRelativePath);
        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("当前内容路径无法定位语言版本目录。");
        }

        var fileName = Path.GetFileName(normalized);
        if (!fileName.StartsWith("index", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("语言版本只能从目录型 Markdown 内容创建。");
        }

        return $"{directory}/index.{targetLanguage}.md";
    }

    private static void EnsureLanguageCanBeWrittenToVariantFile(string languageCode)
    {
        if (languageCode.Contains("..", StringComparison.Ordinal) ||
            languageCode.IndexOfAny(InvalidLanguageFileNameChars) >= 0 ||
            languageCode.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("目标语言代码不能安全地写入 index.{language}.md 文件名。");
        }
    }

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', '/').TrimStart('/');

    private static bool SameCode(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record LanguageVersionContext(
        ContentSummary Current,
        IReadOnlyList<ContentSummary> Variants,
        LocalizationSettingsView Settings);
}

/// <summary>编辑器右侧语言版本小组件需要的完整只读上下文。</summary>
public sealed record ContentLanguageVersionsView(
    ContentLanguageVariantView Current,
    IReadOnlyList<ContentLanguageVariantView> Variants,
    IReadOnlyList<LanguageRecord> AvailableLanguages,
    ContentLanguageVariantView? TranslationSource,
    string PrimaryLanguageCode);

/// <summary>一个可在编辑器中展示和跳转的内容语言版本。</summary>
public sealed record ContentLanguageVariantView(
    string ContentId,
    string? Title,
    string RelativePath,
    string LanguageCode,
    string LanguageLabel,
    bool IsCurrent,
    bool IsTranslation,
    string? SourceLanguageCode,
    string? SourceContentId);

/// <summary>创建语言版本时从编辑器提交给服务层的参数。</summary>
public sealed record CreateContentLanguageVariantRequest(
    string CurrentRelativePath,
    string TargetLanguage,
    bool CopyCurrentContent,
    bool IsTranslation,
    string? SourceContentId);

/// <summary>语言版本创建成功后的导航目标。</summary>
public sealed record ContentLanguageVariantCreateResult(string RelativePath);
