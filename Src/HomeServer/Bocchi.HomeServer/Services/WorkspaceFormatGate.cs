using Bocchi.Workspace;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 读取已有 <c>site.yaml</c> 的 <c>formatVersion</c>，决定这次操作能不能改 workspace 或数据库。
/// 文件不存在或空白不算拒绝，调用方可以初始化。YAML 解析失败也不在这里拦截，避免把坏文件误报成版本问题。
/// </summary>
internal static class WorkspaceFormatGate
{
    /// <summary>
    /// 版本比当前程序新，或显式小于 1 时返回拒绝；否则返回 null。
    /// </summary>
    public static async Task<WorkspaceWriteResult?> TryRefuseAsync(string siteSettingsFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(siteSettingsFile))
        {
            return null;
        }

        var yaml = await File.ReadAllTextAsync(siteSettingsFile, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        if (WorkspaceFormatVersion.TryRead(yaml, out _, out var code, out var message) || code is null)
        {
            return null;
        }

        return WorkspaceWriteResult.Fail(code, message ?? code);
    }
}
