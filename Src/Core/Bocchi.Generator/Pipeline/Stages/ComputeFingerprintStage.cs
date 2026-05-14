using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bocchi.Generator.Exceptions;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 计算本次构建的全局指纹：把所有源 markdown 字节 + frontmatter 文本 + media SHA + site.yaml + 选项扁平化为一个稳定字节串，
/// 再 SHA-256 取小写十六进制。详见 <c>Docs/Milestones/M3/M3.md §3.7</c>。
/// </summary>
public sealed class ComputeFingerprintStage : IBuildStage
{
    public string Name => nameof(ComputeFingerprintStage);

    public Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Graph is null)
        {
            throw new BuildPipelineException($"{Name} 需要内容图。");
        }

        var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendLine(sha, $"bocchi-fingerprint:v1");
        AppendLine(sha, $"mode={session.Options.Mode}");
        AppendLine(sha, $"env={session.Options.Environment}");
        AppendLine(sha, $"includeDrafts={session.Options.IncludeDrafts}");
        AppendLine(sha, $"feedItemCount={session.Graph.Site.Settings.FeedItemCount}");
        AppendLine(sha, $"baseUrl={session.Graph.Site.NormalizedBaseUrl}");

        // 站点设置 / 导航：用全字段 toString 模拟稳定快照
        AppendLine(sha, $"site={session.Graph.Site.Settings}");

        // 内容：每篇按 site-relative-url 排序后 hash 关键字段
        foreach (var post in session.Graph.Posts.OrderBy(p => p.SiteRelativeUrl, StringComparer.Ordinal))
        {
            AppendLine(sha, $"post:{post.SiteRelativeUrl}|st={post.Status}|pub={post.PublishedAt?.UtcTicks}|up={post.UpdatedAt?.UtcTicks}");
            AppendBodyHash(sha, post.BodyMarkdown);
        }

        foreach (var page in session.Graph.Pages.OrderBy(p => p.SiteRelativeUrl, StringComparer.Ordinal))
        {
            AppendLine(sha, $"page:{page.SiteRelativeUrl}|st={page.Status}");
            AppendBodyHash(sha, page.BodyMarkdown);
        }

        foreach (var work in session.Graph.Works.OrderBy(p => p.SiteRelativeUrl, StringComparer.Ordinal))
        {
            AppendLine(sha, $"work:{work.SiteRelativeUrl}|st={work.Status}|featured={work.Featured}");
            AppendBodyHash(sha, work.BodyMarkdown);
        }

        foreach (var note in session.Graph.Notes.OrderBy(p => p.Year + "/" + p.Id, StringComparer.Ordinal))
        {
            AppendLine(sha, $"note:{note.Year}/{note.Id}|st={note.Status}|pub={note.PublishedAt?.UtcTicks}");
            AppendBodyHash(sha, note.BodyMarkdown);
        }

        foreach (var friend in session.Graph.Friends.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            AppendLine(sha, $"friend:{friend.Name}|{friend.Url}|st={friend.Status}|order={friend.Order}");
        }

        foreach (var asset in session.Graph.MediaAssets.OrderBy(a => a.SiteRelativePath, StringComparer.Ordinal))
        {
            AppendLine(sha, $"media:{asset.SiteRelativePath}|sha={asset.Sha256}|size={asset.SizeBytes}");
        }

        var digest = sha.GetCurrentHash();
        var sb = new StringBuilder(digest.Length * 2);
        foreach (var b in digest)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        session.Fingerprint = new BuildFingerprint(sb.ToString());
        session.Log(Name, BuildLogLevel.Info, $"指纹 = {session.Fingerprint.Value}");
        return Task.FromResult(true);
    }

    private static void AppendLine(IncrementalHash sha, string line)
    {
        sha.AppendData(Encoding.UTF8.GetBytes(line));
        sha.AppendData([0x0a]);
    }

    private static void AppendBodyHash(IncrementalHash sha, string body)
    {
        Span<byte> bodyHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(body), bodyHash);
        sha.AppendData(bodyHash);
    }
}
