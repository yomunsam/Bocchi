using Bocchi.HomeServer.Data;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Dashboard 首页 Guide 卡片堆栈服务。卡片定义本身是常量（<see cref="BuiltInDefinitions"/>），
/// 数据库只承担"是否已关闭"和排序，便于 Admin 一键关掉欢迎卡、之后版本广播可继续追加。
/// </summary>
public sealed class DashboardGuideService
{
    /// <summary>欢迎卡片的固定标识。</summary>
    public const string WelcomeKey = "welcome";

    /// <summary>站点语言提示卡的固定标识。</summary>
    public const string SiteLanguageKey = "site-language";

    /// <summary>站点主题提示卡的固定标识。</summary>
    public const string SiteThemeKey = "site-theme";

    /// <summary>编辑页面提示卡的固定标识。</summary>
    public const string EditPageKey = "edit-page";

    /// <summary>Setup 阶段需要确保存在的内置卡片定义。SortOrder 越小越靠前。</summary>
    public static readonly IReadOnlyList<GuideCardDefinition> BuiltInDefinitions =
    [
        new(WelcomeKey, 1),
        new(SiteLanguageKey, 2),
        new(SiteThemeKey, 3),
        new(EditPageKey, 4),
    ];

    private readonly BocchiDbContext _db;
    private readonly TimeProvider _time;

    public DashboardGuideService(BocchiDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>返回当前堆栈中尚未关闭的卡片，按 <c>SortOrder</c> 升序。</summary>
    public async Task<IReadOnlyList<DashboardGuideCardRecord>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _db.DashboardGuideCards
            .AsNoTracking()
            .Where(c => c.DismissedAt == null)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>关闭一张卡片；不存在或已关闭则静默忽略。</summary>
    public async Task DismissAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var card = await _db.DashboardGuideCards
            .FirstOrDefaultAsync(c => c.Key == key, cancellationToken)
            .ConfigureAwait(false);
        if (card is null || card.DismissedAt is not null)
        {
            return;
        }

        card.DismissedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>幂等地确保所有内置 Guide 卡片都已存在；缺失项以未关闭状态补齐。</summary>
    public async Task EnsureBuiltInAsync(CancellationToken cancellationToken = default)
    {
        var existingKeys = await _db.DashboardGuideCards
            .Select(c => c.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingSet = new HashSet<string>(existingKeys, StringComparer.Ordinal);
        var now = _time.GetUtcNow();
        var missing = BuiltInDefinitions.Where(d => !existingSet.Contains(d.Key)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var def in missing)
        {
            _db.DashboardGuideCards.Add(new DashboardGuideCardRecord
            {
                Key = def.Key,
                SortOrder = def.SortOrder,
                CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>内置 Guide 卡片的元数据；文案完全由 i18n 通过 <c>home.guide.&lt;key&gt;.*</c> 渲染。</summary>
    public sealed record GuideCardDefinition(string Key, int SortOrder);
}
