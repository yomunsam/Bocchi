using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Bocchi.Workspace;
using Bocchi.Workspace.Scanning;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 短文创建服务。Dashboard 首页的 Twitter 风组件直接调用：
/// <list type="bullet">
///   <item>写入 <c>notes/&lt;year&gt;/YYYY-MM-DD-HHMM-&lt;slug&gt;.md</c>；</item>
///   <item>frontmatter 写入 <c>id</c>/<c>publishedAt</c>/<c>status: published</c>；</item>
///   <item>写文件后立即触发 <see cref="ContentScanner"/>，让首页"最近更新"立刻可见。</item>
/// </list>
/// </summary>
public sealed partial class NoteCreationService
{
    /// <summary>短文正文最大字符数；与前端 composer 计数器保持同步。</summary>
    public const int MaxBodyLength = 500;

    private readonly BocchiDataLayout _layout;
    private readonly ContentScanner _scanner;
    private readonly TimeProvider _time;

    public NoteCreationService(BocchiDataLayout layout, ContentScanner scanner, TimeProvider time)
    {
        _layout = layout;
        _scanner = scanner;
        _time = time;
    }

    /// <summary>把一条短文落到 workspace 并返回 workspace 内相对路径。</summary>
    public async Task<string> CreateAsync(string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("短文正文不能为空。");
        }
        if (trimmed.Length > MaxBodyLength)
        {
            throw new InvalidOperationException($"短文正文超出 {MaxBodyLength} 字符上限。");
        }

        var now = _time.GetLocalNow();
        var year = now.Year.ToString("D4", CultureInfo.InvariantCulture);
        var slug = BuildSlug(trimmed);
        // 文件名严格按 Loader 期望的 YYYY-MM-DD-HHMM-<slug>.md 格式，确保 NoteLoader 可从中解析出时间。
        var fileBase = $"{now:yyyy-MM-dd-HHmm}-{slug}";

        var notesDir = Path.Combine(_layout.WorkspaceRoot, "notes", year);
        Directory.CreateDirectory(notesDir);

        var fileName = ResolveUniqueFileName(notesDir, fileBase);
        var fullPath = Path.Combine(notesDir, fileName);

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("id: ").Append(Path.GetFileNameWithoutExtension(fileName)).Append('\n');
        sb.Append("publishedAt: ").Append(now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("status: published\n");
        sb.Append("---\n");
        sb.Append(trimmed.Replace("\r\n", "\n"));
        sb.Append('\n');

        await File.WriteAllTextAsync(fullPath, sb.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        // 触发扫描刷新 SQLite 投影，首页"最近更新"立即看得到这条短文。
        await _scanner.ScanAsync(cancellationToken).ConfigureAwait(false);

        return Path.Combine("notes", year, fileName).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// 从正文派生 slug：取第一行前若干个字符，保留 ASCII 字母数字与中日韩字符，
    /// 其余替换为 <c>-</c>；为空时回退到 <c>note</c>。
    /// </summary>
    private static string BuildSlug(string body)
    {
        var firstLine = body.Split('\n')[0].Trim();
        var collapsed = SlugSanitizer().Replace(firstLine, "-").Trim('-');
        if (collapsed.Length == 0)
        {
            return "note";
        }
        // 控制长度避免文件名过长；按 char 截断在 CJK 下也安全。
        if (collapsed.Length > 24)
        {
            collapsed = collapsed[..24].TrimEnd('-');
        }
        return collapsed.ToLowerInvariant();
    }

    private static string ResolveUniqueFileName(string dir, string fileBase)
    {
        var fileName = fileBase + ".md";
        if (!File.Exists(Path.Combine(dir, fileName)))
        {
            return fileName;
        }

        // 极端撞名情况（同分钟同 slug）追加序号。
        for (var i = 2; i < 100; i++)
        {
            fileName = $"{fileBase}-{i}.md";
            if (!File.Exists(Path.Combine(dir, fileName)))
            {
                return fileName;
            }
        }
        throw new InvalidOperationException("无法为新短文分配唯一文件名。");
    }

    /// <summary>合法 slug 字符：ASCII 字母数字与中日韩；其它统一压成 <c>-</c>。</summary>
    [GeneratedRegex(@"[^a-zA-Z0-9\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugSanitizer();
}
