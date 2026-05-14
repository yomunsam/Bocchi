using Bocchi.Generator.Exceptions;
using Bocchi.Generator.ThemeInputs;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>把内容图序列化为 Theme 输入 JSON，并写到 Sink（<see cref="ArtifactKind.ThemeInput"/>）。</summary>
public sealed class WriteThemeInputStage : IBuildStage
{
    private readonly ThemeInputWriter _writer;

    public WriteThemeInputStage(ThemeInputWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    public string Name => nameof(WriteThemeInputStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Graph is null)
        {
            throw new BuildPipelineException($"{Name} 需要内容图。");
        }

        var themeId = session.GetItem<string>(BuildSessionKeys.ThemeId);
        if (string.IsNullOrWhiteSpace(themeId))
        {
            themeId = session.Graph.Site.Settings.DefaultThemeId;
        }

        if (string.IsNullOrWhiteSpace(themeId))
        {
            themeId = "unknown";
        }
        var bocchiVersion = session.GetItem<string>(BuildSessionKeys.BocchiVersion);
        var pairs = _writer.Build(session.Graph, themeId, session.Options.Environment, session.Options.IncludeDrafts, bocchiVersion);
        foreach (var (artifact, _) in pairs)
        {
            await ArtifactSinkHelper.WriteAsync(session, artifact).ConfigureAwait(false);
        }

        session.Log(Name, BuildLogLevel.Info, $"Theme 输入数据 {pairs.Count} 个文件已写入。");
        return true;
    }
}
