namespace Bocchi.Workspace;

/// <summary>
/// <see cref="IWorkspace"/> 的标准实现。内容 workspace 初始化完成意味着源工程目录与 site.yaml 已存在。
/// </summary>
public sealed class Workspace : IWorkspace
{
    public Workspace(WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;
    }

    public WorkspaceLayout Layout { get; }

    public ValueTask<bool> IsInitializedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ok = Directory.Exists(Layout.Root)
            && File.Exists(Layout.SiteSettingsFile);
        return ValueTask.FromResult(ok);
    }
}
