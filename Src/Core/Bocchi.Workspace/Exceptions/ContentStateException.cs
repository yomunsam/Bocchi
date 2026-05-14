namespace Bocchi.Workspace.Exceptions;

/// <summary>SQLite 状态库操作失败时抛出。</summary>
public sealed class ContentStateException : Exception
{
    public ContentStateException(string message)
        : base(message)
    {
    }

    public ContentStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}