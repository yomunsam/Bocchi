namespace Bocchi.Generator.Pipeline;

/// <summary>构建日志级别。</summary>
public enum BuildLogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>单条构建日志。</summary>
public sealed record BuildLog(
    DateTimeOffset OccurredAt,
    string Stage,
    BuildLogLevel Level,
    string Message);