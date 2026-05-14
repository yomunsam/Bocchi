using Bocchi.Generator.State;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 同指纹短路：仅在 <see cref="BuildMode.FullBuild"/> 下生效；最近一次 <c>Succeeded</c> 的 BuildRun
/// 指纹与当前一致时跳过余下阶段。
/// </summary>
public sealed class ShortCircuitIfUpToDateStage : IBuildStage
{
    private readonly IBuildStateStore _store;

    public ShortCircuitIfUpToDateStage(IBuildStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public string Name => nameof(ShortCircuitIfUpToDateStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Options.Mode != BuildMode.FullBuild || session.Options.DisableUpToDateShortCircuit)
        {
            return true;
        }

        if (session.Fingerprint is not { } current)
        {
            return true;
        }

        var latest = await _store.GetLatestSuccessfulRunAsync(session.CancellationToken).ConfigureAwait(false);
        if (latest is { Fingerprint: { } previous, Status: BuildStatus.Succeeded } && string.Equals(previous, current.Value, StringComparison.Ordinal))
        {
            session.ShortCircuited = true;
            session.Log(Name, BuildLogLevel.Info, $"命中 up-to-date（上次 BuildRun #{latest.Id} 指纹相同），跳过余下阶段。");
            return false;
        }

        return true;
    }
}