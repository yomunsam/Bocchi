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

    private readonly BocchiDataLayout _layout;
    private readonly IContentStateStore _store;
    private readonly CategoryTreeService _categories;
    private readonly ThemeSettingsService _themeSettings;
    private readonly SiteProfileSettingsService _siteProfile;
    private readonly DashboardLocalizationService _i18n;

    /// <summary>构造前台 Menu 编辑服务。</summary>
    public NavigationMenuService(
        BocchiDataLayout layout,
        IContentStateStore store,
        CategoryTreeService categories,
        ThemeSettingsService themeSettings,
        SiteProfileSettingsService siteProfile,
        DashboardLocalizationService i18n)
    {
        _layout = layout;
        _store = store;
        _categories = categories;
        _themeSettings = themeSettings;
        _siteProfile = siteProfile;
        _i18n = i18n;
    }

    /// <summary>读取 Menu 编辑视图，并补齐 target 下拉选项和 unresolved warning。</summary>
    public async Task<NavigationMenuEditorView> GetEditorAsync(CancellationToken cancellationToken = default)
    {
        var items = await ReadItemsAsync(cancellationToken).ConfigureAwait(false);
        var targets = await BuildTargetOptionsAsync(items, cancellationToken).ConfigureAwait(false);
        var targetKeys = targets.Select(target => target.Key).ToHashSet(StringComparer.Ordinal);
        var warnings = Flatten(items)
            .Where(item => !targetKeys.Contains(item.TargetKey))
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
        };
    }

    /// <summary>保存编辑器提交的 Menu tree。服务层统一裁剪深度、清理空 id，并写回 YAML。</summary>
    public async Task SaveAsync(IEnumerable<NavigationEditorItem> items, CancellationToken cancellationToken = default)
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
            Label = string.Format(CultureInfo.CurrentCulture, "{0} · {1}", _i18n["siteNavigation.target.group.themePage"], page.DisplayName),
            Available = true,
        }));

        var pages = await _store.ListContentSummariesAsync(ContentKind.Page, cancellationToken).ConfigureAwait(false);
        result.AddRange(pages
            .OrderBy(page => page.Title ?? page.ContentId, StringComparer.OrdinalIgnoreCase)
            .Select(page => new NavigationTargetOption
            {
                Type = "page",
                Value = page.ContentId,
                Label = string.Format(CultureInfo.CurrentCulture, "{0} · {1}", _i18n["siteNavigation.target.group.page"], page.Title ?? page.ContentId),
                Available = true,
            }));

        var categories = await _categories.GetAsync(ContentKind.Post, cancellationToken).ConfigureAwait(false);
        result.AddRange(FlattenCategories(categories.Roots).Select(category => new NavigationTargetOption
        {
            Type = "postCategory",
            Value = category.Slug,
            Label = string.Format(CultureInfo.CurrentCulture, "{0} · {1}", _i18n["siteNavigation.target.group.postCategory"], category.Name),
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
            Label = _i18n[$"siteNavigation.target.builtin.{name}"],
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
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            var item = new NavigationEditorItem
            {
                Id = NormalizeId(ReadScalar(map, "id")),
                Label = ReadScalar(map, "label") ?? string.Empty,
                TargetType = type.Trim(),
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
            .Where(item => !string.IsNullOrWhiteSpace(item.TargetType))
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
                {
                    new YamlScalarNode("target"),
                    new YamlMappingNode
                    {
                        { new YamlScalarNode("type"), new YamlScalarNode(item.TargetType) },
                        { new YamlScalarNode("value"), new YamlScalarNode(item.TargetValue) },
                    }
                },
                { new YamlScalarNode("children"), BuildItemsYaml(item.Children) },
            };
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
}

/// <summary>Blazor 编辑器使用的可变 Menu 节点。</summary>
public sealed class NavigationEditorItem
{
    /// <summary>节点稳定 id。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>展示 label；为空时 Generator 会从 target 自动 fallback。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>target 类型。</summary>
    public string TargetType { get; set; } = "builtin";

    /// <summary>target 值。</summary>
    public string TargetValue { get; set; } = "home";

    /// <summary>子 Menu 项。</summary>
    public List<NavigationEditorItem> Children { get; } = [];

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
