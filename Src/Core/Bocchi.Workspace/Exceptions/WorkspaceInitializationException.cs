namespace Bocchi.Workspace.Exceptions;

/// <summary>工作区初始化阶段失败时抛出。</summary>
public sealed class WorkspaceInitializationException : Exception
{
    public WorkspaceInitializationException(string message)
        : base(message)
    {
    }

    public WorkspaceInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}