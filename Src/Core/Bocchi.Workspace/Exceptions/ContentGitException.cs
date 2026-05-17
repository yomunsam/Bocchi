namespace Bocchi.Workspace.Exceptions;

/// <summary>
/// 内容 workspace Git 操作失败时抛出。包装 <c>LibGit2Sharp</c> 的本机异常，避免向上层泄漏第三方类型。
/// </summary>
public sealed class ContentGitException : Exception
{
    public ContentGitException(string message)
        : base(message)
    {
    }

    public ContentGitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}