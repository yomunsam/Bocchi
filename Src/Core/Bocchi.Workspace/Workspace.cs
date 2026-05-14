namespace Bocchi.Workspace;

/// <summary>
/// <see cref="IWorkspace"/> 的标准实现。M2 阶段把"是否初始化"定义为：
/// 关键系统目录 + 内容空间根 + site.yaml 全部存在。
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
            && Directory.Exists(Layout.BocchiDirectory)
            && Directory.Exists(Layout.ContentSpaceRoot)
            && File.Exists(Layout.ContentSpace.SiteSettingsFile);
        return ValueTask.FromResult(ok);
    }
}
