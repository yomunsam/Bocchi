namespace Bocchi.HomeServer.Hosting;

/// <summary>
/// DataRoot 独占锁。服务运行期间一直持有 <c>&lt;data&gt;/.bocchi.lock</c>，
/// restore 拿不到锁就拒绝执行，避免在运行中的服务底下删库换文件。进程退出时锁自动释放。
/// </summary>
public sealed class DataRootLock : IDisposable
{
    /// <summary>锁文件名，位于 DataRoot 顶层，不进入备份。</summary>
    public const string FileName = ".bocchi.lock";

    private readonly FileStream _stream;

    private DataRootLock(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>尝试获取锁；已被其他进程（或本进程另一处）持有时返回 <c>null</c>。</summary>
    public static DataRootLock? TryAcquire(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        try
        {
            var stream = new FileStream(
                Path.Combine(dataRoot, FileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return new DataRootLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>释放锁。</summary>
    public void Dispose() => _stream.Dispose();
}
