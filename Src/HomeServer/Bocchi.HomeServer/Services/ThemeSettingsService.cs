using Bocchi.HomeServer.Data;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 前台业务 Theme 配置服务。它处理 Theme Contract 配置，不处理 Dashboard 明暗外观。
/// </summary>
public sealed class ThemeSettingsService
{
    private readonly BocchiDbContext _db;
    private readonly TimeProvider _time;

    /// <summary>构造 Theme 设置服务。</summary>
    public ThemeSettingsService(BocchiDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>读取当前默认 Theme 配置；没有配置时返回一个可编辑空配置。</summary>
    public async Task<ThemeConfigurationRecord> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var record = await _db.ThemeConfigurations
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return record ?? new ThemeConfigurationRecord
        {
            ThemeId = "default-svelte",
            ConfigurationJson = "{}",
            UpdatedAt = _time.GetUtcNow(),
        };
    }

    /// <summary>保存当前默认 Theme 配置。</summary>
    public async Task SaveDefaultAsync(string themeId, string configurationJson, CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = string.IsNullOrWhiteSpace(themeId) ? "default-svelte" : themeId.Trim();
        var normalizedJson = string.IsNullOrWhiteSpace(configurationJson) ? "{}" : configurationJson.Trim();
        var record = await _db.ThemeConfigurations
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new ThemeConfigurationRecord();
            _db.ThemeConfigurations.Add(record);
        }

        record.ThemeId = normalizedThemeId;
        record.ConfigurationJson = normalizedJson;
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
