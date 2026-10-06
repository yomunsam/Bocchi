using System.Text.Json;

using Bocchi.Generator.State;
using Bocchi.Workspace;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 同指纹短路：仅在 <see cref="BuildMode.FullBuild"/> 下生效；最近一次 <c>Succeeded</c> 的 BuildRun
/// 指纹与当前一致，且 <c>output/public</c> 里的构建 manifest 也是这份指纹时，才跳过余下阶段。
/// 输出被清空或来自别的构建（例如 restore 之后）时照常构建，避免发布旧站点或空目录。
/// </summary>
public sealed class ShortCircuitIfUpToDateStage : IBuildStage
{
    private readonly IBuildStateStore _store;
    private readonly BocchiDataLayout _layout;

    public ShortCircuitIfUpToDateStage(IBuildStateStore store, BocchiDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(layout);
        _store = store;
        _layout = layout;
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
            if (!OutputMatches(current.Value))
            {
                session.Log(Name, BuildLogLevel.Info, "指纹与上次相同，但输出目录缺少对应的构建 manifest，重新构建。");
                return true;
            }

            session.ShortCircuited = true;
            session.Log(Name, BuildLogLevel.Info, $"命中 up-to-date（上次 BuildRun #{latest.Id} 指纹相同），跳过余下阶段。");
            return false;
        }

        return true;
    }

    /// <summary>检查 <c>output/public/.bocchi-manifest.json</c> 是否存在且指纹一致。</summary>
    private bool OutputMatches(string fingerprint)
    {
        var path = Path.Combine(_layout.PublicOutputDirectory, WriteManifestStage.ManifestPath.TrimStart('/'));
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("fingerprint", out var value)
                && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), fingerprint, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
