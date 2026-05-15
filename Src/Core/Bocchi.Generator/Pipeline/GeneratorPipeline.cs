using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Pipeline.Stages;
using Bocchi.Generator.Sinks;
using Bocchi.Generator.State;

using Microsoft.Extensions.Logging;

namespace Bocchi.Generator.Pipeline;

/// <summary>
/// Generator 流水线主入口。把固定顺序的 <see cref="IBuildStage"/> 按序执行，统一处理异常、日志、持久化。
/// 详见 <c>Docs/Milestones/M3/M3.md §3.8</c>。
/// </summary>
public sealed partial class GeneratorPipeline
{
    private readonly LoadContentStage _load;
    private readonly BuildContentGraphStage _graph;
    private readonly ComputeFingerprintStage _fingerprint;
    private readonly ShortCircuitIfUpToDateStage _shortCircuit;
    private readonly LoadThemeStage _loadTheme;
    private readonly WriteThemeInputStage _writeInput;
    private readonly WriteSiteArtifactsStage _writeSite;
    private readonly CopyMediaStage _copyMedia;
    private readonly RunThemeBuildStage _runTheme;
    private readonly CollectThemeOutputStage _collectThemeOutput;
    private readonly ValidateOutputStage _validate;
    private readonly WriteManifestStage _writeManifest;
    private readonly IBuildStateStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<GeneratorPipeline> _logger;

    public GeneratorPipeline(
        LoadContentStage load,
        BuildContentGraphStage graph,
        ComputeFingerprintStage fingerprint,
        ShortCircuitIfUpToDateStage shortCircuit,
        LoadThemeStage loadTheme,
        WriteThemeInputStage writeInput,
        WriteSiteArtifactsStage writeSite,
        CopyMediaStage copyMedia,
        RunThemeBuildStage runTheme,
        CollectThemeOutputStage collectThemeOutput,
        ValidateOutputStage validate,
        WriteManifestStage writeManifest,
        IBuildStateStore store,
        TimeProvider time,
        ILogger<GeneratorPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(shortCircuit);
        ArgumentNullException.ThrowIfNull(loadTheme);
        ArgumentNullException.ThrowIfNull(writeInput);
        ArgumentNullException.ThrowIfNull(writeSite);
        ArgumentNullException.ThrowIfNull(copyMedia);
        ArgumentNullException.ThrowIfNull(runTheme);
        ArgumentNullException.ThrowIfNull(collectThemeOutput);
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(writeManifest);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _load = load;
        _graph = graph;
        _fingerprint = fingerprint;
        _shortCircuit = shortCircuit;
        _loadTheme = loadTheme;
        _writeInput = writeInput;
        _writeSite = writeSite;
        _copyMedia = copyMedia;
        _runTheme = runTheme;
        _collectThemeOutput = collectThemeOutput;
        _validate = validate;
        _writeManifest = writeManifest;
        _store = store;
        _time = time;
        _logger = logger;
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "构建开始 SessionId={SessionId} Mode={Mode} Env={Env}")]
    private partial void LogBuildStart(Guid sessionId, BuildMode mode, string env);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "构建结束 SessionId={SessionId} Status={Status} Duration={DurationMs}ms")]
    private partial void LogBuildEnd(Guid sessionId, BuildStatus status, double durationMs);

    /// <summary>执行一次构建。</summary>
    /// <param name="options">本次选项。</param>
    /// <param name="sink">输出端。</param>
    /// <param name="themeId">主题 id（可空，<c>null</c> 时回退到 <c>site.yaml</c> 的 <c>defaultThemeId</c>）。</param>
    /// <param name="bocchiVersion">Bocchi 自身版本号，会写入 BuildRuns / .bocchi-manifest.json / build-context.json。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<BuildResult> RunAsync(
        BuildOptions options,
        IBuildSink sink,
        string? themeId,
        string? bocchiVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sink);

        var sessionId = Guid.NewGuid();
        var startedAt = _time.GetUtcNow();
        var session = new BuildSession(sessionId, options, sink, startedAt, cancellationToken);
        if (themeId is not null)
        {
            session.SetItem(BuildSessionKeys.ThemeId, themeId);
        }

        if (bocchiVersion is not null)
        {
            session.SetItem(BuildSessionKeys.BocchiVersion, bocchiVersion);
        }

        session.SetItem("buildStateStore", _store);

        LogBuildStart(sessionId, options.Mode, options.Environment);

        // 阶段固定顺序，详见 M3.md §3.7
        IBuildStage[] stages =
        [
            _load,
            _graph,
            _fingerprint,
            _shortCircuit,
            _loadTheme,
            _writeInput,
            _writeSite,
            _copyMedia,
            _runTheme,
            _collectThemeOutput,
            _writeManifest,
            _validate,
        ];

        BuildStatus status = BuildStatus.Failed;
        string? reason = null;
        var persistedLogCount = 0;
        try
        {
            await ExecuteAsync(_load, session).ConfigureAwait(false);
            if (options.Mode == BuildMode.FullBuild)
            {
                session.BuildRunId = await _store.BeginRunAsync(session, themeId, bocchiVersion, cancellationToken).ConfigureAwait(false);
                persistedLogCount = await PersistLogsAsync(session, persistedLogCount).ConfigureAwait(false);
            }

            for (var i = 1; i < stages.Length; i++)
            {
                var stage = stages[i];
                cancellationToken.ThrowIfCancellationRequested();
                var continueRun = await ExecuteAsync(stage, session).ConfigureAwait(false);
                persistedLogCount = await PersistLogsAsync(session, persistedLogCount).ConfigureAwait(false);
                if (!continueRun)
                {
                    break;
                }
            }

            status = session.ShortCircuited ? BuildStatus.Skipped : BuildStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            reason = "Cancelled";
            session.Log("GeneratorPipeline", BuildLogLevel.Warning, "构建被取消。");
            persistedLogCount = await PersistLogsAsync(session, persistedLogCount).ConfigureAwait(false);
            throw;
        }
        catch (GeneratorException ex)
        {
            reason = ex.Message;
            session.Log("GeneratorPipeline", BuildLogLevel.Error, $"构建失败：{ex.Message}");
            persistedLogCount = await PersistLogsAsync(session, persistedLogCount).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            reason = $"Unexpected: {ex.Message}";
            session.Log("GeneratorPipeline", BuildLogLevel.Error, $"未预期异常：{ex}");
            persistedLogCount = await PersistLogsAsync(session, persistedLogCount).ConfigureAwait(false);
            throw;
        }

        var finishedAt = _time.GetUtcNow();
        var result = new BuildResult
        {
            SessionId = sessionId,
            Mode = options.Mode,
            Status = status,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Fingerprint = session.Fingerprint,
            Reason = reason,
            Logs = session.Logs,
            Artifacts = session.Artifacts,
            BuildRunId = session.BuildRunId,
        };
        await _store.CompleteRunAsync(result, cancellationToken).ConfigureAwait(false);
        LogBuildEnd(sessionId, status, result.Duration.TotalMilliseconds);
        return result;
    }

    private static async Task<bool> ExecuteAsync(IBuildStage stage, BuildSession session)
    {
        try
        {
            return await stage.ExecuteAsync(session).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Log(stage.Name, BuildLogLevel.Error, $"阶段失败：{ex.Message}");
            throw;
        }
    }

    private async Task<int> PersistLogsAsync(BuildSession session, int alreadyPersisted)
    {
        if (session.BuildRunId is not { } runId)
        {
            return alreadyPersisted;
        }

        var snapshot = session.Logs;
        for (var i = alreadyPersisted; i < snapshot.Count; i++)
        {
            await _store.AppendLogAsync(runId, snapshot[i], session.CancellationToken).ConfigureAwait(false);
        }

        return snapshot.Count;
    }
}
