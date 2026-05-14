using System.Text.Json;

using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Sinks;
using Bocchi.Generator.ThemeInputs;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>对已写入的 artifact 做结构校验，并在完整文件系统构建中清理孤儿输出。</summary>
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

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // 只验证非过滤模式（OnlyArtifactPath 模式下落到 Sink 的 artifact 不完整是预期的）
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

            ValidateManifest(session);
            CleanupOrphanPublicFiles(session);
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
        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    private static void ValidateManifest(BuildSession session)
    {
        var manifestArtifact = session.Artifacts.SingleOrDefault(
            a => string.Equals(a.Path, WriteManifestStage.ManifestPath, StringComparison.Ordinal));
        if (manifestArtifact is null)
        {
            throw new BuildPipelineException($"必需的构建 manifest '{WriteManifestStage.ManifestPath}' 未生成。");
        }

        if (manifestArtifact.Bytes is not { } bytes)
        {
            throw new BuildPipelineException("构建 manifest 必须以内存字节形式生成，便于校验。");
        }

        WriteManifestStage.BuildManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<WriteManifestStage.BuildManifest>(bytes.Span, ThemeInputWriter.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BuildPipelineException("构建 manifest 不是合法 JSON。", ex);
        }

        if (manifest is null)
        {
            throw new BuildPipelineException("构建 manifest 反序列化为空。");
        }

        var expected = session.Artifacts
            .Where(a => !string.Equals(a.Path, WriteManifestStage.ManifestPath, StringComparison.Ordinal))
            .OrderBy(a => a.Path, StringComparer.Ordinal)
            .ToArray();
        var actual = manifest.Artifacts.OrderBy(a => a.Path, StringComparer.Ordinal).ToArray();
        if (actual.Length != expected.Length)
        {
            throw new BuildPipelineException($"构建 manifest 条目数不一致：expected={expected.Length}, actual={actual.Length}。");
        }

        for (var i = 0; i < expected.Length; i++)
        {
            var e = expected[i];
            var a = actual[i];
            if (!string.Equals(a.Path, e.Path, StringComparison.Ordinal)
                || !string.Equals(a.Kind, e.Kind.ToString(), StringComparison.Ordinal)
                || !string.Equals(a.ContentType, e.ContentType, StringComparison.Ordinal)
                || a.SizeBytes != e.SizeBytes
                || !string.Equals(a.Sha256, e.Sha256, StringComparison.Ordinal)
                || !string.Equals(a.ProducedBy, e.ProducedBy, StringComparison.Ordinal))
            {
                throw new BuildPipelineException($"构建 manifest 条目不一致：'{e.Path}'。");
            }
        }
    }

    private static void CleanupOrphanPublicFiles(BuildSession session)
    {
        if (session.Options.Mode != BuildMode.FullBuild || session.Sink is not FileSystemBuildSink sink)
        {
            return;
        }

        var publicRoot = Path.GetFullPath(sink.PublicRoot);
        if (!Directory.Exists(publicRoot))
        {
            return;
        }

        var expectedPublicFiles = session.Artifacts
            .Where(a => a.Kind is ArtifactKind.SiteArtifact or ArtifactKind.Media or ArtifactKind.ThemeOutput)
            .Select(a => Path.GetFullPath(Path.Combine(publicRoot, a.Path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(publicRoot, "*", SearchOption.AllDirectories))
        {
            var absolute = Path.GetFullPath(file);
            if (!IsUnderRoot(absolute, publicRoot))
            {
                throw new BuildPipelineException($"输出文件 '{absolute}' 不在输出根 '{publicRoot}' 下。");
            }

            if (!expectedPublicFiles.Contains(absolute))
            {
                File.Delete(absolute);
                session.Log(nameof(ValidateOutputStage), BuildLogLevel.Warning,
                    $"已清理未登记的输出文件：{Path.GetRelativePath(publicRoot, absolute).Replace(Path.DirectorySeparatorChar, '/')}");
            }
        }
    }

    private static bool IsUnderRoot(string absolute, string root)
    {
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return absolute.StartsWith(rootWithSep, StringComparison.Ordinal) ||
               string.Equals(absolute, root, StringComparison.Ordinal);
    }
}