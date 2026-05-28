using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Bocchi.Generator.Utilities;

/// <summary>Generator 内部统一的 SHA-256 小写十六进制编码工具。</summary>
internal static class Sha256Hex
{
    /// <summary>计算内存字节的 SHA-256，并返回小写十六进制字符串。</summary>
    internal static string FromBytes(ReadOnlyMemory<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes.Span, hash);
        return ToLowerHex(hash);
    }

    /// <summary>计算文件内容的 SHA-256，并返回小写十六进制字符串。</summary>
    internal static string FromFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return ToLowerHex(sha.ComputeHash(stream));
    }

    /// <summary>异步计算文件内容的 SHA-256，并返回小写十六进制字符串。</summary>
    internal static async Task<string> FromFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return ToLowerHex(hash);
    }

    /// <summary>把已有 hash bytes 编码为小写十六进制；不重新计算 digest。</summary>
    internal static string ToLowerHex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
