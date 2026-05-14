namespace Bocchi.Workspace.Tests;

/// <summary>
/// 一个临时目录辅助器：构造时创建一个唯一目录，Dispose 时删除。
/// 用于让每个测试拥有独立的 workspace。
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "bocchi-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Layout = new WorkspaceLayout(Root);
    }

    public string Root { get; }

    public WorkspaceLayout Layout { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }
}
