using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.HomeServer.Build;
using Bocchi.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bocchi.HomeServer;

/// <summary>
/// 把构建相关的最小命令行入口封装到 HomeServer 进程：<br/>
/// <c>Bocchi.HomeServer -- build [--theme=&lt;id&gt;] [--include-drafts] [--env=&lt;name&gt;]</c>
/// </summary>
public static partial class BuildCli
{
    /// <summary>解析 CLI 参数。返回 <c>true</c> 表示用户请求构建（应跳过 web 服务）。</summary>
    public static bool TryParse(string[] args, out BuildCliOptions options)
    {
        options = new BuildCliOptions();
        if (args.Length == 0 || !string.Equals(args[0], "build", StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--theme=", StringComparison.Ordinal))
            {
                options.ThemeId = arg["--theme=".Length..];
            }
            else if (arg.StartsWith("--env=", StringComparison.Ordinal))
            {
                options.Environment = arg["--env=".Length..];
            }
            else if (string.Equals(arg, "--include-drafts", StringComparison.Ordinal))
            {
                options.IncludeDrafts = true;
            }
        }

        return true;
    }

    /// <summary>执行一次完整构建。</summary>
    public static async Task<int> RunAsync(IServiceProvider services, BuildCliOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var layout = services.GetRequiredService<WorkspaceLayout>();
        var pipeline = services.GetRequiredService<GeneratorPipeline>();
        var logger = services.GetRequiredService<ILogger<BuildOrchestrator>>();
        var sink = new FileSystemBuildSink(layout);

        var buildOptions = new BuildOptions
        {
            Mode = BuildMode.FullBuild,
            Environment = options.Environment,
            IncludeDrafts = options.IncludeDrafts,
        };
        var result = await pipeline.RunAsync(
            buildOptions, sink, options.ThemeId, BuildOrchestrator.ResolveBocchiVersion(), cancellationToken).ConfigureAwait(false);

        BuildCliLog.Done(logger, result.Status, result.SessionId, result.Duration.TotalSeconds);
        return result.Status == BuildStatus.Failed ? 1 : 0;
    }

    private static partial class BuildCliLog
    {
        [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Bocchi build CLI finished: status={Status} session={SessionId} duration={Seconds:F2}s")]
        public static partial void Done(ILogger logger, BuildStatus status, Guid sessionId, double seconds);
    }
}

/// <summary>CLI 的解析结果。</summary>
public sealed class BuildCliOptions
{
    public string? ThemeId { get; set; }

    public string Environment { get; set; } = "production";

    public bool IncludeDrafts { get; set; }
}
