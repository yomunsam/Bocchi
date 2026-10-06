using System.Globalization;

using Bocchi.HomeServer.Data;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 后台统一的时间显示：库里存的是 UTC，按站点资料里的时区换算，并带上「UTC+8」这样的短标签。
/// 站点时区缺失或无效时退回服务器本地时区。组件在初始化时调用一次 <see cref="LoadAsync"/>。
/// </summary>
public sealed class DashboardTimeFormatter
{
    private readonly IDbContextFactory<BocchiDbContext> _dbFactory;
    private TimeZoneInfo? _zone;

    /// <summary>构造时间格式化服务。</summary>
    public DashboardTimeFormatter(IDbContextFactory<BocchiDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>当前使用的时区；加载前是服务器本地时区。</summary>
    public TimeZoneInfo Zone => _zone ?? TimeZoneInfo.Local;

    /// <summary>读取站点时区；同一个作用域内只读一次。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_zone is not null)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var timeZoneId = await db.SiteProfileSettings.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        _zone = ResolveZone(timeZoneId);
    }

    /// <summary>日期加时间加时区标签，例如 2026-10-06 15:14 (UTC+8)。</summary>
    public string FormatDateTime(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, Zone);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{local:yyyy-MM-dd HH:mm} ({FormatUtcOffset(local.Offset)})");
    }

    /// <summary>UTC 的 <see cref="DateTime"/>（Kind 未指定时也按 UTC 处理）。</summary>
    public string FormatDateTime(DateTime utc)
        => FormatDateTime(new DateTimeOffset(DateTime.SpecifyKind(utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc, DateTimeKind.Utc)));

    /// <summary>只显示站点时区下的日期。</summary>
    public string FormatDate(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(value, Zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>把时区 ID 解析成 <see cref="TimeZoneInfo"/>；无效时用服务器本地时区。</summary>
    internal static TimeZoneInfo ResolveZone(string? timeZoneId)
        => !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out var zone)
            ? zone
            : TimeZoneInfo.Local;

    /// <summary>和前台一致的紧凑 offset 文本：UTC、UTC+8、UTC+5:30。</summary>
    internal static string FormatUtcOffset(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
        {
            return "UTC";
        }

        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var duration = offset.Duration();
        var hours = (int)duration.TotalHours;
        return duration.Minutes == 0
            ? string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{hours}")
            : string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{hours}:{duration.Minutes:00}");
    }
}
