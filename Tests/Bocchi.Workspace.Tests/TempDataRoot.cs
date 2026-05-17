namespace Bocchi.Workspace.Tests;

/// <summary>
/// 一个临时目录辅助器：构造时创建一个唯一目录，Dispose 时删除。
/// 用于让每个测试拥有独立的 DataRoot。
/// </summary>
public sealed class TempDataRoot : IDisposable
{
    public TempDataRoot()
    {
        Root = Path.Combine(Path.GetTempPath(), "bocchi-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Layout = new BocchiDataLayout(Root);
    }

    public string Root { get; }

    public BocchiDataLayout Layout { get; }

    public void Dispose()
    {
        DeleteBestEffort(Root);
    }

    private static void DeleteBestEffort(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
