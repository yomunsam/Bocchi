namespace Bocchi.HomeServer.Services;

/// <summary>
/// 构建要用的分类或本地化快照不可用：<c>site.yaml</c> 版本过新，或对应 workspace 文件损坏。
/// 调用方应让这次构建失败，而不是退回数据库里的旧投影。
/// </summary>
public sealed class WorkspaceSnapshotException : Exception
{
    /// <summary>用已经写好的拒绝原因构造异常。</summary>
    public WorkspaceSnapshotException(string message)
        : base(message)
    {
    }
}
