using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Bocchi.Workspace;
using Bocchi.Workspace.Scanning;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 短文创建服务。Dashboard 首页的 Twitter 风组件直接调用：
/// <list type="bullet">
///   <item>写入 <c>notes/&lt;yyyy&gt;/&lt;MMdd&gt;/&lt;HHmm&gt;-&lt;id&gt;/index.md</c>；</item>
///   <item>frontmatter 写入 8 位短 <c>id</c>/<c>publishedAt</c>/<c>status: published</c>；</item>
///   <item>写文件后立即触发 <see cref="ContentScanner"/>，让首页"最近更新"立刻可见。</item>
/// </list>
/// </summary>
public sealed partial class NoteCreationService
{
    /// <summary>短文正文最大字符数；与前端 composer 计数器保持同步。</summary>
    public const int MaxBodyLength = 500;

    private const int NoteIdLength = 8;
    private const string NoteIdAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

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
        var monthDay = now.ToString("MMdd", CultureInfo.InvariantCulture);
        var time = now.ToString("HHmm", CultureInfo.InvariantCulture);
        var (id, noteDirectory) = CreateUniqueNoteDirectory(year, monthDay, time);
        Directory.CreateDirectory(noteDirectory);
        Directory.CreateDirectory(Path.Combine(noteDirectory, "assets"));
        var fullPath = Path.Combine(noteDirectory, "index.md");

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("id: ").Append(id).Append('\n');
        sb.Append("publishedAt: ").Append(now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("status: published\n");
        sb.Append("---\n");
        sb.Append(trimmed.Replace("\r\n", "\n"));
        sb.Append('\n');

        await File.WriteAllTextAsync(fullPath, sb.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        // 触发扫描刷新 SQLite 投影，首页"最近更新"立即看得到这条短文。
        await _scanner.ScanAsync(cancellationToken).ConfigureAwait(false);

        return Path.GetRelativePath(_layout.WorkspaceRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>生成短 id 并创建目标目录名；同一分钟撞 id 时重新生成，不给时间追加兼容后缀。</summary>
    private (string Id, string Directory) CreateUniqueNoteDirectory(string year, string monthDay, string time)
    {
        var parent = Path.Combine(_layout.WorkspaceRoot, "notes", year, monthDay);
        Directory.CreateDirectory(parent);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var id = CreateNoteId();
            var directory = Path.Combine(parent, $"{time}-{id}");
            if (!Directory.Exists(directory))
            {
                return (id, directory);
            }
        }

        throw new InvalidOperationException("无法为新短文分配唯一 id。");
    }

    private static string CreateNoteId()
    {
        Span<char> buffer = stackalloc char[NoteIdLength];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = NoteIdAlphabet[RandomNumberGenerator.GetInt32(NoteIdAlphabet.Length)];
        }

        return new string(buffer);
    }
}
