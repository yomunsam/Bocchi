using System.Text.Json;

using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 前台业务 Theme 配置服务。它处理 Theme Contract 配置，不处理 Dashboard 明暗外观。
/// </summary>
public sealed class ThemeSettingsService
{
    private readonly BocchiDbContext _db;
    private readonly TimeProvider _time;
    private readonly WorkspaceLayout _layout;

    /// <summary>构造 Theme 设置服务。</summary>
    public ThemeSettingsService(BocchiDbContext db, TimeProvider time, WorkspaceLayout layout)
    {
        _db = db;
        _time = time;
        _layout = layout;
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
            ThemeId = "default-static",
            ConfigurationJson = "{}",
            UpdatedAt = _time.GetUtcNow(),
        };
    }

    /// <summary>保存当前默认 Theme 配置。</summary>
    public async Task SaveDefaultAsync(string themeId, string configurationJson, CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = string.IsNullOrWhiteSpace(themeId) ? "default-static" : themeId.Trim();
        var normalizedJson = NormalizeConfigurationJson(configurationJson);
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
        await WriteThemeConfigFileAsync(normalizedThemeId, normalizedJson, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteThemeConfigFileAsync(string themeId, string configurationJson, CancellationToken cancellationToken)
    {
        var path = ResolveThemeConfigPath(themeId);
        Directory.CreateDirectory(_layout.ThemeConfigDirectory);
        await File.WriteAllTextAsync(path, configurationJson, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveThemeConfigPath(string themeId)
    {
        if (themeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            themeId.Contains('/') ||
            themeId.Contains('\\') ||
            string.Equals(themeId, ".", StringComparison.Ordinal) ||
            string.Equals(themeId, "..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Theme id '{themeId}' 不能作为 Theme 配置文件名。");
        }

        return Path.Combine(_layout.ThemeConfigDirectory, themeId + ".json");
    }

    private static string NormalizeConfigurationJson(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(configurationJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Theme 配置必须是 JSON object。");
        }

        return document.RootElement.GetRawText();
    }
}
