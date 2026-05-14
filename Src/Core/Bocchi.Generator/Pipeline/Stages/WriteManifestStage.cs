using System.Globalization;
using System.Text.Json;
using Bocchi.Generator.ThemeInputs;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>把所有 artifact 的元数据写成 <c>/build-manifest.json</c>（<see cref="ArtifactKind.SiteArtifact"/>）。</summary>
public sealed class WriteManifestStage : IBuildStage
{
    public string Name => nameof(WriteManifestStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var manifest = new BuildManifest
        {
            SessionId = session.SessionId.ToString("D", CultureInfo.InvariantCulture),
            Fingerprint = session.Fingerprint?.Value,
            GeneratedAt = DateTimeOffset.UtcNow,
            BocchiVersion = session.GetItem<string>(BuildSessionKeys.BocchiVersion),
            ThemeId = session.GetItem<string>(BuildSessionKeys.ThemeId),
            Environment = session.Options.Environment,
            Artifacts = session.Artifacts
                .Where(a => !string.Equals(a.Path, "/build-manifest.json", StringComparison.Ordinal))
                .Select(a => new ManifestEntry(a.Path, a.Kind.ToString(), a.ContentType, a.SizeBytes, a.Sha256, a.ProducedBy))
                .OrderBy(e => e.Path, StringComparer.Ordinal)
                .ToArray(),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ThemeInputWriter.JsonOptions);
        var sha = Sha256Util.Hex(bytes);
        var artifact = new BuildArtifact
        {
            Path = "/build-manifest.json",
            Kind = ArtifactKind.SiteArtifact,
            ContentType = "application/json; charset=utf-8",
            SizeBytes = bytes.Length,
            Sha256 = sha,
            ProducedBy = Name,
            Bytes = bytes,
        };
        await ArtifactSinkHelper.WriteAsync(session, artifact).ConfigureAwait(false);
        session.Log(Name, BuildLogLevel.Info, $"build-manifest.json 已写入（{manifest.Artifacts.Length} 条）。");
        return true;
    }

    private sealed record BuildManifest
    {
        public required string SessionId { get; init; }
        public string? Fingerprint { get; init; }
        public required DateTimeOffset GeneratedAt { get; init; }
        public string? BocchiVersion { get; init; }
        public string? ThemeId { get; init; }
        public required string Environment { get; init; }
        public required ManifestEntry[] Artifacts { get; init; }
    }

    private sealed record ManifestEntry(string Path, string Kind, string ContentType, long SizeBytes, string Sha256, string ProducedBy);
}

internal static class Sha256Util
{
    public static string Hex(ReadOnlyMemory<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(bytes.Span, hash);
        var sb = new System.Text.StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
