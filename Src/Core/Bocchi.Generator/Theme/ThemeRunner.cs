using System.Diagnostics;
using System.Runtime.InteropServices;

using Bocchi.Generator.Pipeline;
using Bocchi.GeneratorContract;
using Bocchi.Theme.DefaultStatic;

namespace Bocchi.Generator.Theme;

/// <summary>默认 <see cref="IThemeRunner"/>。支持 M5 内置模板 runner，并保留旧版 <c>build.command</c> 兼容路径。</summary>
public sealed class ThemeRunner : IThemeRunner
{
    /// <inheritdoc />
    public async Task RunAsync(
        ThemeRunInvocation invocation,
        Action<BuildLogLevel, string> onLog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(onLog);

        if (IsBuiltinTemplate(invocation.Manifest))
        {
            await RunBuiltinTemplateAsync(invocation, onLog, cancellationToken).ConfigureAwait(false);
            return;
        }

        var processRunner = ResolveProcessRunner(invocation.Manifest);
        if (invocation.RunInstall && !string.IsNullOrWhiteSpace(processRunner.InstallCommand))
        {
            await ExecuteAsync(processRunner.InstallCommand!, "install", invocation, onLog, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(processRunner.Command, "build", invocation, onLog, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>判断 manifest 是否声明为内置模板 runner。</summary>
    private static bool IsBuiltinTemplate(ThemeManifest manifest)
        => manifest.Runner is not null &&
           string.Equals(manifest.Runner.Kind.Trim(), "builtin-template", StringComparison.OrdinalIgnoreCase);

    /// <summary>运行受信任的内置默认模板 renderer。</summary>
    private static async Task RunBuiltinTemplateAsync(
        ThemeRunInvocation invocation,
        Action<BuildLogLevel, string> onLog,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(invocation.Manifest.Id, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal))
        {
            throw new ThemeRunnerException($"builtin-template runner 暂时只支持 '{DefaultStaticThemeDefinition.ThemeId}'。");
        }

        try
        {
            await DefaultStaticTemplateRenderer.RenderAsync(
                new DefaultStaticRenderRequest
                {
                    ThemeRoot = invocation.ThemeRoot,
                    InputDirectory = invocation.InputDirectoryAbsolute,
                    OutputDirectory = invocation.OutputDirectoryAbsolute,
                    Manifest = invocation.Manifest,
                    BaseUrl = invocation.BaseUrl,
                    Environment = invocation.Environment,
                },
                cancellationToken).ConfigureAwait(false);
            onLog(BuildLogLevel.Info, "[builtin-template] default-static rendered.");
        }
        catch (DefaultStaticThemeException ex)
        {
            throw new ThemeRunnerException($"Theme '{invocation.Manifest.Id}' builtin-template 渲染失败：{ex.Message}", ex);
        }
    }

    /// <summary>把新旧 Theme manifest 统一解析成 process runner 命令；非 process runner 交给后续内置 renderer 实现。</summary>
    internal static ResolvedProcessRunner ResolveProcessRunner(ThemeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Runner is null)
        {
            if (manifest.Build is null)
            {
                throw new ThemeRunnerException($"Theme '{manifest.Id}' 未声明 runner，也没有旧版 build.command。");
            }

            return new ResolvedProcessRunner(manifest.Build.Command, manifest.Build.InstallCommand);
        }

        var kind = manifest.Runner.Kind.Trim();
        if (!string.Equals(kind, "process", StringComparison.OrdinalIgnoreCase))
        {
            throw new ThemeRunnerException($"Theme '{manifest.Id}' runner.kind='{manifest.Runner.Kind}' 尚未由当前 Generator 支持。");
        }

        var command = string.IsNullOrWhiteSpace(manifest.Runner.Command)
            ? manifest.Build?.Command
            : manifest.Runner.Command;
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ThemeRunnerException($"Theme '{manifest.Id}' 的 process runner 缺少 command。");
        }

        var installCommand = string.IsNullOrWhiteSpace(manifest.Runner.InstallCommand)
            ? manifest.Build?.InstallCommand
            : manifest.Runner.InstallCommand;
        return new ResolvedProcessRunner(command!, installCommand);
    }

    private static async Task ExecuteAsync(
        string commandLine,
        string label,
        ThemeRunInvocation invocation,
        Action<BuildLogLevel, string> onLog,
        CancellationToken cancellationToken)
    {
        var (fileName, args) = SplitCommandLine(commandLine);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = invocation.ThemeRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["BOCCHI_INPUT_DIR"] = invocation.InputDirectoryAbsolute;
        psi.Environment["BOCCHI_OUTPUT_DIR"] = invocation.OutputDirectoryAbsolute;
        psi.Environment["BOCCHI_THEME_ID"] = invocation.Manifest.Id;
        psi.Environment["BOCCHI_BASE_URL"] = invocation.BaseUrl;
        psi.Environment["BOCCHI_ENVIRONMENT"] = invocation.Environment;

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                throw new ThemeRunnerException($"Theme '{invocation.Manifest.Id}' {label} 进程未能启动。");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ThemeRunnerException(
                $"Theme '{invocation.Manifest.Id}' {label}: 无法启动 '{fileName}': {ex.Message}", ex);
        }

        var stdoutTask = ForwardAsync(process.StandardOutput, BuildLogLevel.Info, label, onLog, cancellationToken);
        var stderrTask = ForwardAsync(process.StandardError, BuildLogLevel.Warning, label, onLog, cancellationToken);

        using var timeoutCts = new CancellationTokenSource(invocation.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            throw new ThemeRunnerException($"Theme '{invocation.Manifest.Id}' {label} 超时（>{invocation.Timeout.TotalMinutes:F1} min）。");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new ThemeRunnerException(
                $"Theme '{invocation.Manifest.Id}' {label} 退出码 {process.ExitCode}（命令: {commandLine}）。");
        }
    }

    private static async Task ForwardAsync(
        StreamReader reader, BuildLogLevel level, string label, Action<BuildLogLevel, string> onLog, CancellationToken cancellationToken)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                onLog(level, $"[{label}] {line}");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 进程可能已经在退出；不再级联抛出
        }
    }

    /// <summary>把"command arg1 arg2"形式的字符串切分为 (filename, args)。支持双引号包围的实参。</summary>
    internal static (string FileName, IReadOnlyList<string> Args) SplitCommandLine(string commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in commandLine)
        {
            switch (ch)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ' ' when !inQuotes:
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }

                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        if (parts.Count == 0)
        {
            throw new ThemeRunnerException($"Theme 命令行为空：'{commandLine}'");
        }

        return (parts[0], parts.Skip(1).ToList());
    }

    /// <summary>cross-platform 探针：当前操作系统下用于回显 "ok" 的命令（测试辅助，避免依赖 dotnet/node）。</summary>
    public static string EchoCommandForCurrentOs(string text)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"cmd /c echo {text}"
            : $"/bin/sh -c \"echo {text}\"";
    }

    /// <summary>已解析的 process runner 命令组。</summary>
    internal sealed record ResolvedProcessRunner(string Command, string? InstallCommand);
}
