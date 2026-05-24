using System.Globalization;

using Bocchi.ContentModel;
using Bocchi.Workspace;
using Bocchi.Workspace.State;

using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>Dashboard 前台 Menu 编辑服务。它读写 workspace 的 <c>site/navigation.yaml</c> 正式 Menu tree。</summary>
public sealed class NavigationMenuService
{
    /// <summary>Menu 最大深度，与 Category tree 保持一致。</summary>
    public const int MaxDepth = CategoryTreeService.MaxDepth;

    private const string CommonAboutLabel = "i18n://common@menu.about";

    /// <summary>Preset 识别 About 页面时采用的保守 slug 候选，按最常见命名优先。</summary>
    private static readonly string[] AboutPageSlugCandidates = ["about", "aboutme", "about-me"];

    private readonly BocchiDataLayout _layout;
    private readonly IContentStateStore _store;
    private readonly CategoryTreeService _categories;
    private readonly ThemeSettingsService _themeSettings;
    private readonly SiteProfileSettingsService _siteProfile;
    private readonly LocalizationSettingsService _localization;
    private readonly DashboardLocalizationService _i18n;

    /// <summary>构造前台 Menu 编辑服务。</summary>
    public NavigationMenuService(
        BocchiDataLayout layout,
        IContentStateStore store,
        CategoryTreeService categories,
        ThemeSettingsService themeSettings,
        SiteProfileSettingsService siteProfile,
        LocalizationSettingsService localization,
        DashboardLocalizationService i18n)
    {
        _layout = layout;
        _store = store;
        _categories = categories;
        _themeSettings = themeSettings;
        _siteProfile = siteProfile;
        _localization = localization;
        _i18n = i18n;
    }

    /// <summary>读取 Menu 编辑视图，并补齐 target 下拉选项和 unresolved warning。</summary>
    public async Task<NavigationMenuEditorView> GetEditorAsync(CancellationToken cancellationToken = default)
    {
        var items = await ReadItemsAsync(cancellationToken).ConfigureAwait(false);
        var targets = await BuildTargetOptionsAsync(items, cancellationToken).ConfigureAwait(false);
        var localization = await _localization.GetAsync(cancellationToken).ConfigureAwait(false);
        var targetKeys = targets
            .Where(target => target.Available)
            .Select(target => target.Key)
            .ToHashSet(StringComparer.Ordinal);
        var warnings = Flatten(items)
            .Where(item => item.HasTarget && !targetKeys.Contains(item.TargetKey))
            .Select(item => new NavigationMenuWarning
            {
                ItemId = item.Id,
                Label = string.IsNullOrWhiteSpace(item.Label) ? item.TargetValue : item.Label,
                TargetType = item.TargetType,
                TargetValue = item.TargetValue,
            })
            .ToArray();
        return new NavigationMenuEditorView
        {
            Items = items,
            TargetOptions = targets,
            Warnings = warnings,
            EnabledLanguages = localization.EnabledLanguages,
            CommonTextOverrides = localization.CommonTextOverrides,
        };
    }

    /// <summary>
    /// 在空 Menu 上应用 Bocchi 默认预设。非空时不覆盖用户已有结构，避免把显式清空或手工编辑误当初始化。
    /// </summary>
    public async Task<bool> ApplyDefaultPresetAsync(CancellationToken cancellationToken = default)
    {
        var current = await ReadItemsAsync(cancellationToken).ConfigureAwait(false);
        if (current.Count > 0)
        {
            return false;
        }

        var pages = await _store.ListContentSummariesAsync(ContentKind.Page, cancellationToken).ConfigureAwait(false);
        var aboutSlug = FindAboutPageSlug(pages);
        await SaveAsync(CreateDefaultPresetItems(aboutSlug), cancellationToken: cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>保存编辑器提交的 Menu tree。服务层统一裁剪深度、清理空 id，并写回 YAML。</summary>
    public async Task SaveAsync(
        IEnumerable<NavigationEditorItem> items,
        IEnumerable<CommonI18nTextOverride>? commonTextOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var normalized = NormalizeEditorItems(items, depth: 0);
        var root = new YamlMappingNode
        {
            { new YamlScalarNode("items"), BuildItemsYaml(normalized) },
        };
        var stream = new YamlStream(new YamlDocument(root));
        Directory.CreateDirectory(_layout.Workspace.SiteDirectory);
        await using var output = File.Create(_layout.Workspace.NavigationFile);
        await using var writer = new StreamWriter(output);
        stream.Save(writer, assignAnchors: false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (commonTextOverrides is not null)
        {
            await _localization.SaveCommonTextOverridesAsync(commonTextOverrides, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<NavigationEditorItem>> ReadItemsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_layout.Workspace.NavigationFile))
        {
            return [];
        }

        await using var input = File.OpenRead(_layout.Workspace.NavigationFile);
        using var reader = new StreamReader(input);
        var raw = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var stream = new YamlStream();
        using var stringReader = new StringReader(raw);
        stream.Load(stringReader);
        if (stream.Documents.Count == 0)
        {
            return [];
        }

        return stream.Documents[0].RootNode switch
        {
            YamlSequenceNode seq => ParseItems(seq, depth: 0),
            YamlMappingNode map when TryGetSequence(map, "items") is { } seq => ParseItems(seq, depth: 0),
            _ => [],
        };
    }

    private async Task<List<NavigationTargetOption>> BuildTargetOptionsAsync(
        IReadOnlyList<NavigationEditorItem> items,
        CancellationToken cancellationToken)
    {
        var result = new List<NavigationTargetOption>
        {
            NoTarget(),
            Builtin("home"),
            Builtin("posts"),
            Builtin("works"),
            Builtin("notes"),
            Builtin("friends"),
        };

        var contract = await GetActiveThemeContractAsync(cancellationToken).ConfigureAwait(false);
        result.AddRange(contract.SpecialPages.Select(page => new NavigationTargetOption
        {
            Type = "themePage",
            Value = page.Name,
            GroupLabel = _i18n["siteNavigation.target.group.themePage"],
            Label = string.Format(CultureInfo.CurrentCulture, "{0} · {1}", _i18n["siteNavigation.target.group.themePage"], page.DisplayName),
            Available = true,
        }));

        var pages = await _store.ListContentSummariesAsync(ContentKind.Page, cancellationToken).ConfigureAwait(false);
        result.AddRange(pages
            .Select(page => new { Summary = page, Slug = TryReadPageSlug(page.RelativePath) })
            .Where(page => !string.IsNullOrWhiteSpace(page.Slug))
            .GroupBy(page => page.Slug!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(page => page.Summary.IsTranslation).ThenBy(page => page.Summary.Language).First())
            .OrderBy(page => page.Summary.Title ?? page.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(page => new NavigationTargetOption
            {
                Type = "page",
                Value = page.Slug!,
                GroupLabel = _i18n["siteNavigation.target.group.page"],
                Label = string.Format(CultureInfo.CurrentCulture, "{0} · {1}", _i18n["siteNavigation.target.group.page"], page.Summary.Title ?? page.Slug),
                Available = true,
            }));

        var categories = await _categories.GetAsync(ContentKind.Post, cancellationToken).ConfigureAwait(false);
        var displayLang = _i18n.CurrentLanguage.Code;
        result.AddRange(FlattenCategories(categories.Roots).Select(category => new NavigationTargetOption
        {
            Type = "postCategory",
            Value = category.Slug,
            GroupLabel = _i18n["siteNavigation.target.group.postCategory"],
            Label = string.Format(CultureInfo.CurrentCulture, "{0} · {1}", _i18n["siteNavigation.target.group.postCategory"], PickLocalizedName(category, displayLang)),
            Available = true,
        }));

        var existing = result.Select(option => option.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in Flatten(items))
        {
            if (existing.Add(item.TargetKey))
            {
                result.Add(new NavigationTargetOption
                {
                    Type = item.TargetType,
                    Value = item.TargetValue,
                    GroupLabel = _i18n["siteNavigation.target.group.unavailable"],
                    Label = string.Format(CultureInfo.CurrentCulture, "{0}:{1} · {2}", item.TargetType, item.TargetValue, _i18n["siteNavigation.target.unavailable"]),
                    Available = false,
                });
            }
        }

        return result;

        NavigationTargetOption Builtin(string name) => new()
        {
            Type = "builtin",
            Value = name,
            GroupLabel = _i18n["siteNavigation.target.group.builtin"],
            Label = _i18n[$"siteNavigation.target.builtin.{name}"],
            Available = true,
        };

        NavigationTargetOption NoTarget() => new()
        {
            Type = string.Empty,
            Value = string.Empty,
            GroupLabel = _i18n["siteNavigation.target.group.none"],
            Label = _i18n["siteNavigation.target.none"],
            Available = true,
        };
    }

    private async Task<ThemePageContractView> GetActiveThemeContractAsync(CancellationToken cancellationToken)
    {
        var themeId = (await _siteProfile.GetAsync(cancellationToken).ConfigureAwait(false)).DefaultThemeId;
        if (string.IsNullOrWhiteSpace(themeId))
        {
            themeId = (await _themeSettings.GetDefaultAsync(cancellationToken).ConfigureAwait(false)).ThemeId;
        }

        return await _themeSettings
            .GetPageContractAsync(themeId, _i18n.CurrentLanguage.Code, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>创建默认 Menu preset；About 找不到真实 Page 时保留为无 target 待配置项。</summary>
    private static IReadOnlyList<NavigationEditorItem> CreateDefaultPresetItems(string? aboutSlug)
    {
        var about = new NavigationEditorItem
        {
            Id = "about",
            Label = CommonAboutLabel,
        };
        if (!string.IsNullOrWhiteSpace(aboutSlug))
        {
            about.TargetType = "page";
            about.TargetValue = aboutSlug;
        }

        return
        [
            BuiltinPresetItem("home"),
            BuiltinPresetItem("posts"),
            BuiltinPresetItem("notes"),
            BuiltinPresetItem("works"),
            about,
        ];

        static NavigationEditorItem BuiltinPresetItem(string name) => new()
        {
            Id = name,
            TargetType = "builtin",
            TargetValue = name,
        };
    }

    private static string? FindAboutPageSlug(IEnumerable<ContentSummary> pages)
    {
        var slugs = pages
            .Select(page => TryReadPageSlug(page.RelativePath))
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return AboutPageSlugCandidates.FirstOrDefault(slugs.Contains);
    }

    private static string? TryReadPageSlug(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3
            && string.Equals(parts[0], "pages", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }

    private static List<NavigationEditorItem> ParseItems(YamlSequenceNode seq, int depth)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }

        var result = new List<NavigationEditorItem>();
        for (var i = 0; i < seq.Children.Count; i++)
        {
            if (seq.Children[i] is not YamlMappingNode map)
            {
                continue;
            }

            var target = TryGetMapping(map, "target");
            var type = target is null ? string.Empty : ReadScalar(target, "type");
            var item = new NavigationEditorItem
            {
                Id = NormalizeId(ReadScalar(map, "id")),
                Label = ReadScalar(map, "label") ?? string.Empty,
                TargetType = type?.Trim() ?? string.Empty,
                TargetValue = target is null ? string.Empty : ReadScalar(target, "value") ?? string.Empty,
            };
            if (TryGetSequence(map, "children") is { } children)
            {
                item.Children.AddRange(ParseItems(children, depth + 1));
            }

            result.Add(item);
        }

        return result;
    }

    private static List<NavigationEditorItem> NormalizeEditorItems(IEnumerable<NavigationEditorItem> items, int depth)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }

        return items
            .Select(item =>
            {
                var normalized = new NavigationEditorItem
                {
                    Id = NormalizeId(item.Id),
                    Label = item.Label?.Trim() ?? string.Empty,
                    TargetType = item.TargetType.Trim(),
                    TargetValue = item.TargetValue?.Trim() ?? string.Empty,
                };
                normalized.Children.AddRange(NormalizeEditorItems(item.Children, depth + 1));
                return normalized;
            })
            .ToList();
    }

    private static YamlSequenceNode BuildItemsYaml(IEnumerable<NavigationEditorItem> items)
    {
        var seq = new YamlSequenceNode();
        foreach (var item in items)
        {
            var map = new YamlMappingNode
            {
                { new YamlScalarNode("id"), new YamlScalarNode(item.Id) },
                { new YamlScalarNode("label"), new YamlScalarNode(item.Label) },
                { new YamlScalarNode("children"), BuildItemsYaml(item.Children) },
            };
            if (item.HasTarget)
            {
                map.Add(
                    new YamlScalarNode("target"),
                    new YamlMappingNode
                    {
                        { new YamlScalarNode("type"), new YamlScalarNode(item.TargetType) },
                        { new YamlScalarNode("value"), new YamlScalarNode(item.TargetValue) },
                    });
            }

            seq.Add(map);
        }

        return seq;
    }

    private static IEnumerable<NavigationEditorItem> Flatten(IEnumerable<NavigationEditorItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<CategoryTreeNode> FlattenCategories(IEnumerable<CategoryTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenCategories(node.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>按当前 UI 语言取分类的多语言显示名；缺翻译或翻译为空白时回退到原 <see cref="CategoryTreeNode.Name"/>。</summary>
    private static string PickLocalizedName(CategoryTreeNode node, string langCode)
    {
        if (node.LocalizedNames is { Count: > 0 } map
            && !string.IsNullOrEmpty(langCode)
            && map.TryGetValue(langCode, out var localized)
            && !string.IsNullOrWhiteSpace(localized))
        {
            return localized.Trim();
        }

        return node.Name;
    }

    private static string NormalizeId(string? value)
        => string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim();

    private static string? ReadScalar(YamlMappingNode map, string key)
        => map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private static YamlMappingNode? TryGetMapping(YamlMappingNode map, string key)
        => map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlMappingNode child ? child : null;

    private static YamlSequenceNode? TryGetSequence(YamlMappingNode map, string key)
        => map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode child ? child : null;
}

/// <summary>Menu 编辑器完整视图。</summary>
public sealed class NavigationMenuEditorView
{
    /// <summary>当前 Menu tree。</summary>
    public required List<NavigationEditorItem> Items { get; init; }

    /// <summary>当前可选 target 列表，包含 unavailable 保留项。</summary>
    public required IReadOnlyList<NavigationTargetOption> TargetOptions { get; init; }

    /// <summary>当前 Menu 中无法解析到有效内容或 Theme special page 的 target。</summary>
    public required IReadOnlyList<NavigationMenuWarning> Warnings { get; init; }

    /// <summary>站点当前启用语言，用于在导航页内编辑 Common i18n 文案。</summary>
    public required IReadOnlyList<LanguageRecord> EnabledLanguages { get; init; }

    /// <summary>已保存的 Common i18n 覆盖；导航页保存时会完整带回，避免清掉其他页面维护的 key。</summary>
    public required IReadOnlyList<CommonI18nTextOverride> CommonTextOverrides { get; init; }
}

/// <summary>Blazor 编辑器使用的可变 Menu 节点。</summary>
public sealed class NavigationEditorItem
{
    /// <summary>节点稳定 id。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>展示 label；为空时 Generator 会从 target 自动 fallback。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>target 类型。</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>target 值。</summary>
    public string TargetValue { get; set; } = string.Empty;

    /// <summary>子 Menu 项。</summary>
    public List<NavigationEditorItem> Children { get; } = [];

    /// <summary>是否已经选择语义 target；无 target 节点只作为后台待配置项或前台分组使用。</summary>
    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetType);

    /// <summary>HTML select 使用的复合 key。</summary>
    public string TargetKey
    {
        get => NavigationTargetOption.CreateKey(TargetType, TargetValue);
        set
        {
            var parsed = NavigationTargetOption.ParseKey(value);
            TargetType = parsed.Type;
            TargetValue = parsed.Value;
        }
    }
}

/// <summary>Menu target 下拉框选项。</summary>
public sealed class NavigationTargetOption
{
    /// <summary>target 类型。</summary>
    public required string Type { get; init; }

    /// <summary>target 值。</summary>
    public required string Value { get; init; }

    /// <summary>Dashboard 展示标签。</summary>
    public required string Label { get; init; }

    /// <summary>Dashboard 下拉分组标签。</summary>
    public required string GroupLabel { get; init; }

    /// <summary>该 target 在当前内容和 Theme 下是否可解析。</summary>
    public required bool Available { get; init; }

    /// <summary>HTML select 使用的复合 key。</summary>
    public string Key => CreateKey(Type, Value);

    /// <summary>创建 HTML select 使用的复合 key。</summary>
    public static string CreateKey(string type, string value)
        => type.Trim() + ":" + Uri.EscapeDataString(value?.Trim() ?? string.Empty);

    /// <summary>解析 HTML select 提交的复合 key。</summary>
    public static (string Type, string Value) ParseKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return ("builtin", "home");
        }

        var separator = key.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            return (key.Trim(), string.Empty);
        }

        return (key[..separator].Trim(), Uri.UnescapeDataString(key[(separator + 1)..]));
    }
}

/// <summary>Menu target 无法在当前内容或 Theme 下解析时的提示。</summary>
public sealed class NavigationMenuWarning
{
    /// <summary>发生 warning 的 Menu item id。</summary>
    public required string ItemId { get; init; }

    /// <summary>该 Menu item 的展示 label 或 target 值。</summary>
    public required string Label { get; init; }

    /// <summary>无法解析的 target 类型。</summary>
    public required string TargetType { get; init; }

    /// <summary>无法解析的 target 值。</summary>
    public required string TargetValue { get; init; }
}
