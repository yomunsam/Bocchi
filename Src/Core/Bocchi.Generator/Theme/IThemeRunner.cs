using Bocchi.Generator.Pipeline;

namespace Bocchi.Generator.Theme;

/// <summary>
/// Theme 运行参数。
/// </summary>
public sealed record ThemeRunInvocation
{
    public required string ThemeRoot { get; init; }
    public required GeneratorContract.ThemeManifest Manifest { get; init; }
    public required string InputDirectoryAbsolute { get; init; }
    public required string OutputDirectoryAbsolute { get; init; }
    public required string BaseUrl { get; init; }
    public required string Environment { get; init; }
    public required bool RunInstall { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>抽象 Theme 运行器，便于测试替换。</summary>
public interface IThemeRunner
{
    /// <summary>执行 Theme 构建。stdout / stderr 会通过 <paramref name="onLog"/> 转发；非零退出码抛 <see cref="ThemeRunnerException"/>。</summary>
    Task RunAsync(ThemeRunInvocation invocation, Action<BuildLogLevel, string> onLog, CancellationToken cancellationToken);
}