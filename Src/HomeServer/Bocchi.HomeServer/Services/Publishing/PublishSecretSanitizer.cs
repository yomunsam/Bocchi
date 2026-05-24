namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>发布错误信息脱敏工具，避免远端异常把 token 带进 UI 或数据库。</summary>
public static class PublishSecretSanitizer
{
    /// <summary>替换消息中出现的明文凭据片段。</summary>
    public static string Sanitize(string? message, params string?[] secrets)
    {
        var result = string.IsNullOrWhiteSpace(message) ? "Unknown publish error." : message;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                continue;
            }

            result = result.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        return result;
    }
}
