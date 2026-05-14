using System.Globalization;

namespace Bocchi.Workspace.Content;

/// <summary>
/// 时间字段解析。统一以 <see cref="DateTimeOffset"/> 表示。缺少时区时使用回退时区。
/// </summary>
internal static class DateTimeFieldParser
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-ddTHH:mm:sszzz",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
    ];

    public static bool TryParse(string raw, TimeSpan fallbackOffset, out DateTimeOffset value)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            // DateTimeOffset.TryParse 默认对无时区视为本地，需要重排到 fallbackOffset 表示的时区
            if (!ContainsTimeZone(raw))
            {
                var dt = DateTime.SpecifyKind(parsed.DateTime, DateTimeKind.Unspecified);
                value = new DateTimeOffset(dt, fallbackOffset);
                return true;
            }

            value = parsed;
            return true;
        }

        if (DateTime.TryParseExact(raw, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt2))
        {
            var unspecified = DateTime.SpecifyKind(dt2, DateTimeKind.Unspecified);
            value = new DateTimeOffset(unspecified, fallbackOffset);
            return true;
        }

        value = default;
        return false;
    }

    private static bool ContainsTimeZone(string raw)
        => raw.Contains('Z', StringComparison.OrdinalIgnoreCase)
           || raw.Contains('+', StringComparison.Ordinal)
           // a "-" inside the date part is fine; check that at least one "-" appears AFTER an 'T' or a space
           || HasNegativeOffset(raw);

    private static bool HasNegativeOffset(string raw)
    {
        var sepIdx = raw.IndexOfAny(['T', ' ']);
        if (sepIdx < 0)
        {
            return false;
        }

        return raw.IndexOf('-', sepIdx) >= 0;
    }
}
