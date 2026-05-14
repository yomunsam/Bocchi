namespace Bocchi.Workspace;

/// <summary><see cref="IWorkspaceLoader"/> 的标准实现：构造一个 <see cref="Workspace"/>。</summary>
public sealed class WorkspaceLoader : IWorkspaceLoader
{
    public ValueTask<IWorkspace> OpenAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        cancellationToken.ThrowIfCancellationRequested();
        IWorkspace ws = new Workspace(new WorkspaceLayout(rootPath));
        return ValueTask.FromResult(ws);
    }
}
