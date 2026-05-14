using Bocchi.Generator.Exceptions;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>对已写入的 artifact 做轻量结构校验。</summary>
public sealed class ValidateOutputStage : IBuildStage
{
    public string Name => nameof(ValidateOutputStage);

    private static readonly string[] RequiredThemeInputs =
    [
        "/site.json",
        "/navigation.json",
        "/posts.json",
        "/pages.json",
        "/works.json",
        "/notes.json",
        "/friends.json",
        "/photos.json",
        "/build-context.json",
    ];

    public Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // 只验证非过滤模式（OnlyArtifactPath 模式下 artifact 集合不完整是预期的）
        if (string.IsNullOrEmpty(session.Options.OnlyArtifactPath))
        {
            var produced = session.Artifacts
                .Where(a => a.Kind == ArtifactKind.ThemeInput)
                .Select(a => a.Path)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var required in RequiredThemeInputs)
            {
                if (!produced.Contains(required))
                {
                    throw new BuildPipelineException($"必需的 Theme 输入 '{required}' 未生成。");
                }
            }
        }

        // 所有 SiteArtifact 的 size 必须 > 0
        foreach (var artifact in session.Artifacts.Where(a => a.Kind == ArtifactKind.SiteArtifact))
        {
            if (artifact.SizeBytes <= 0)
            {
                throw new BuildPipelineException($"站点产物 '{artifact.Path}' 字节数为 0。");
            }
        }

        session.Log(Name, BuildLogLevel.Info, $"产物校验通过：共 {session.Artifacts.Count} 个 artifact。");
        return Task.FromResult(true);
    }
}
