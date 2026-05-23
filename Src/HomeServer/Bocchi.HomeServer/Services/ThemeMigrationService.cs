namespace Bocchi.HomeServer.Services;

/// <summary>
/// Theme 切换迁移服务：扫描 Menu 中 <c>i18n://theme@*</c> 引用，
/// 让 Dashboard 在切换 Theme 前对每个引用做明确决策（保留 / 转纯文本 / 转 Common / 映射到新 Theme key）。
/// </summary>
public sealed class ThemeMigrationService
{
    /// <summary>Menu Label 中 Theme 引用前缀。</summary>
    public const string ThemeRefPrefix = "i18n://theme@";

    /// <summary>Menu Label 中 Common 引用前缀。</summary>
    public const string CommonRefPrefix = "i18n://common@";

    private readonly NavigationMenuService _nav;
    private readonly ThemeSettingsService _themeSettings;
    private readonly LocalizationSettingsService _localization;
    private readonly SiteProfileSettingsService _siteProfile;

    /// <summary>构造迁移服务。</summary>
    public ThemeMigrationService(
        NavigationMenuService nav,
        ThemeSettingsService themeSettings,
        LocalizationSettingsService localization,
        SiteProfileSettingsService siteProfile)
    {
        _nav = nav;
        _themeSettings = themeSettings;
        _localization = localization;
        _siteProfile = siteProfile;
    }

    /// <summary>扫描当前 Menu，收集所有 Theme i18n 引用并比对新 Theme manifest。</summary>
    public async Task<ThemeMigrationPlan> ScanAsync(
        string fromThemeId,
        string toThemeId,
        CancellationToken cancellationToken = default)
    {
        var nav = await _nav.GetEditorAsync(cancellationToken).ConfigureAwait(false);
        var oldI18n = await _themeSettings.GetI18nAsync(fromThemeId, cancellationToken).ConfigureAwait(false);
        var newI18n = await _themeSettings.GetI18nAsync(toThemeId, cancellationToken).ConfigureAwait(false);

        // 老 Theme 中可用的多语言文本：manifest 默认值与用户覆盖值合并；用户覆盖优先。
        var oldDefaults = oldI18n.Keys.ToDictionary(x => x.Key, x => x.DefaultValues, StringComparer.Ordinal);
        var oldOverrides = oldI18n.TextOverrides.ToDictionary(x => x.Key, x => x.Values, StringComparer.Ordinal);
        var newKeySet = new HashSet<string>(newI18n.Keys.Select(k => k.Key), StringComparer.Ordinal);
        var newKeyList = newI18n.Keys.Select(k => k.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var enabledLanguages = nav.EnabledLanguages.Select(x => x.Code).ToArray();
        var primaryLanguage = enabledLanguages.FirstOrDefault() ?? "en-US";

        var entries = new List<ThemeMigrationEntry>();
        foreach (var (item, path) in Flatten(nav.Items, parentPath: Array.Empty<string>()))
        {
            if (!item.Label.StartsWith(ThemeRefPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var oldKey = item.Label[ThemeRefPrefix.Length..].Trim();
            if (oldKey.Length == 0)
            {
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (oldDefaults.TryGetValue(oldKey, out var defaults))
            {
                foreach (var pair in defaults)
                {
                    values[pair.Key] = pair.Value;
                }
            }
            if (oldOverrides.TryGetValue(oldKey, out var overrides))
            {
                foreach (var pair in overrides)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            entries.Add(new ThemeMigrationEntry
            {
                ItemId = item.Id,
                Location = path.Length == 0 ? oldKey : string.Join(" / ", path),
                OldKey = oldKey,
                OldValues = values,
                ExistsInNewTheme = newKeySet.Contains(oldKey),
            });
        }

        return new ThemeMigrationPlan
        {
            FromThemeId = fromThemeId,
            ToThemeId = toThemeId,
            Entries = entries,
            EnabledLanguages = enabledLanguages,
            PrimaryLanguage = primaryLanguage,
            NewThemeKeys = newKeyList,
        };
    }

    /// <summary>按 Dashboard 提交的决策改写 Menu 并切换默认 Theme。</summary>
    public async Task ApplyAsync(
        ThemeMigrationPlan plan,
        IReadOnlyDictionary<string, ThemeMigrationDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(decisions);

        var nav = await _nav.GetEditorAsync(cancellationToken).ConfigureAwait(false);
        var commonOverrides = nav.CommonTextOverrides.ToDictionary(
            x => x.Key,
            x => new Dictionary<string, string>(x.Values, StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);

        var byId = new Dictionary<string, NavigationEditorItem>(StringComparer.Ordinal);
        foreach (var (item, _) in Flatten(nav.Items, Array.Empty<string>()))
        {
            byId[item.Id] = item;
        }

        foreach (var entry in plan.Entries)
        {
            // 缺少决策时默认保留原值，避免误改。
            if (!decisions.TryGetValue(entry.ItemId, out var decision))
            {
                continue;
            }
            if (!byId.TryGetValue(entry.ItemId, out var item))
            {
                continue;
            }

            switch (decision)
            {
                case ThemeMigrationDecision.KeepAsIs:
                    break;

                case ThemeMigrationDecision.ToPlainText plain:
                    item.Label = ResolvePlainText(entry, plain.Language, plan.PrimaryLanguage);
                    break;

                case ThemeMigrationDecision.ToCommonI18n common:
                    var commonKey = common.NewKey.Trim();
                    if (commonKey.Length == 0)
                    {
                        break;
                    }
                    item.Label = CommonRefPrefix + commonKey;
                    if (!commonOverrides.TryGetValue(commonKey, out var bag))
                    {
                        bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        commonOverrides[commonKey] = bag;
                    }
                    foreach (var pair in entry.OldValues)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Value))
                        {
                            bag[pair.Key] = pair.Value;
                        }
                    }
                    break;

                case ThemeMigrationDecision.MapToNewThemeKey mapped:
                    var mappedKey = mapped.NewKey.Trim();
                    if (mappedKey.Length == 0)
                    {
                        break;
                    }
                    item.Label = ThemeRefPrefix + mappedKey;
                    break;
            }
        }

        var commonOverrideList = commonOverrides
            .Where(x => x.Value.Count > 0)
            .Select(x => new CommonI18nTextOverride
            {
                Key = x.Key,
                Values = x.Value,
            })
            .ToArray();
        await _nav.SaveAsync(nav.Items, commonOverrideList, cancellationToken).ConfigureAwait(false);

        // 完成菜单改写后再切换 Theme，避免切换中途出现指向新 Theme 的悬空引用。
        var site = await _siteProfile.GetAsync(cancellationToken).ConfigureAwait(false);
        await _siteProfile.SaveAsync(new SiteProfileSettingsUpdate
        {
            SiteName = site.SiteName,
            DefaultTitle = site.DefaultTitle,
            Description = site.Description,
            PublicBaseUrl = site.PublicBaseUrl,
            CopyrightNotice = site.CopyrightNotice,
            Language = site.Language,
            TimeZone = site.TimeZone,
            DefaultThemeId = plan.ToThemeId,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolvePlainText(ThemeMigrationEntry entry, string language, string primaryLanguage)
    {
        if (entry.OldValues.TryGetValue(language, out var explicitValue) && !string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }
        if (entry.OldValues.TryGetValue(primaryLanguage, out var primaryValue) && !string.IsNullOrWhiteSpace(primaryValue))
        {
            return primaryValue;
        }
        var first = entry.OldValues.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return first ?? entry.OldKey;
    }

    private static IEnumerable<(NavigationEditorItem Item, string[] Path)> Flatten(
        IEnumerable<NavigationEditorItem> items,
        string[] parentPath)
    {
        foreach (var item in items)
        {
            var label = string.IsNullOrWhiteSpace(item.Label) ? item.TargetValue : item.Label;
            var path = parentPath.Length == 0
                ? new[] { label }
                : [.. parentPath, label];
            yield return (item, path);
            foreach (var nested in Flatten(item.Children, path))
            {
                yield return nested;
            }
        }
    }
}

/// <summary>迁移扫描结果。</summary>
public sealed class ThemeMigrationPlan
{
    /// <summary>原 Theme id。</summary>
    public required string FromThemeId { get; init; }

    /// <summary>目标 Theme id。</summary>
    public required string ToThemeId { get; init; }

    /// <summary>需要决策的引用条目；空表示直接切换不需要向导。</summary>
    public required IReadOnlyList<ThemeMigrationEntry> Entries { get; init; }

    /// <summary>站点当前启用语言（按主语言在前的顺序）。</summary>
    public required IReadOnlyList<string> EnabledLanguages { get; init; }

    /// <summary>主语言代码，回退到 en-US。</summary>
    public required string PrimaryLanguage { get; init; }

    /// <summary>目标 Theme 声明的所有 i18n key，供 MapToNewThemeKey 选择。</summary>
    public required IReadOnlyList<string> NewThemeKeys { get; init; }
}

/// <summary>单个 Theme i18n 引用条目。</summary>
public sealed class ThemeMigrationEntry
{
    /// <summary>对应 NavigationEditorItem 的稳定 id。</summary>
    public required string ItemId { get; init; }

    /// <summary>用于在向导中展示位置（按父级 Label 串成的路径）。</summary>
    public required string Location { get; init; }

    /// <summary>老 Theme 中的 i18n key（不含 prefix）。</summary>
    public required string OldKey { get; init; }

    /// <summary>老 Theme 解析出的语言→文本字典，包含 manifest 默认值与用户覆盖值。</summary>
    public required IReadOnlyDictionary<string, string> OldValues { get; init; }

    /// <summary>目标 Theme 是否仍然声明该 key。</summary>
    public required bool ExistsInNewTheme { get; init; }
}

/// <summary>迁移决策基类。</summary>
public abstract record ThemeMigrationDecision
{
    private ThemeMigrationDecision() { }

    /// <summary>不改写：引用维持指向老 Theme key（适用于新 Theme 兼容声明）。</summary>
    public sealed record KeepAsIs : ThemeMigrationDecision;

    /// <summary>改写为指定语言的纯文本，丢弃多语言能力。</summary>
    public sealed record ToPlainText(string Language) : ThemeMigrationDecision;

    /// <summary>改写为 <c>i18n://common@*</c>，并把老 Theme 的多语言值写入 Common 覆盖。</summary>
    public sealed record ToCommonI18n(string NewKey) : ThemeMigrationDecision;

    /// <summary>改写为新 Theme 中的另一 key（不复制文本）。</summary>
    public sealed record MapToNewThemeKey(string NewKey) : ThemeMigrationDecision;
}
