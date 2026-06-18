using Bocchi.HomeServer.Data;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Dashboard 偏好设置服务。它只处理后台体验，不管理站点基础约定。
/// </summary>
public sealed class DashboardSettingsService
{
    private readonly BocchiDbContext _db;
    private readonly TimeProvider _time;

    /// <summary>构造 Dashboard 设置服务。</summary>
    public DashboardSettingsService(BocchiDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>读取单站点 Dashboard 设置；缺失时创建默认值。</summary>
    public async Task<DashboardSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.DashboardSettings
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (settings is not null)
        {
            return settings;
        }

        settings = new DashboardSettings { Id = 1, UpdatedAt = _time.GetUtcNow() };
        _db.DashboardSettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return settings;
    }

    /// <summary>保存 Dashboard 明暗外观偏好。</summary>
    public async Task SaveAsync(string appearanceMode, CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        settings.AppearanceMode = NormalizeAppearance(appearanceMode);
        settings.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeAppearance(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "auto",
        };
}
