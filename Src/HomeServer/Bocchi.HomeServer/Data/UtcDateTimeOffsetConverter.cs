using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bocchi.HomeServer.Data;

/// <summary>
/// DateTimeOffset ↔ UTC ticks。SQLite 里存成可排序的 INTEGER，比较和排序按真实先后；
/// 读出来的 offset 固定是 +00:00，需要按站点时区展示的地方自行转换。
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, long>
{
    /// <summary>创建转换器。</summary>
    public UtcDateTimeOffsetConverter()
        : base(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
    {
    }
}
