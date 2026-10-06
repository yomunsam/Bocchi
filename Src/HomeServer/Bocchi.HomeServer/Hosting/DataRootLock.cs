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

    /// <summary>
    /// 尝试获取锁；已被其他进程（或本进程另一处）持有时返回 <c>null</c>。
    /// 锁文件打不开（比如属主是 root）时抛 <see cref="BocchiStartupException"/>。
    /// </summary>
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
        catch (UnauthorizedAccessException)
        {
            // 常见原因是用 root 跑过 restore，锁文件归 root 所有
            throw new BocchiStartupException(
                $"没有权限打开锁文件 {Path.Combine(dataRoot, FileName)}，它可能属于另一个用户（比如 root）。"
                + "请检查数据目录的属主，例如 chown -R bocchi:bocchi <数据目录>。");
        }
    }

    /// <summary>释放锁。</summary>
    public void Dispose() => _stream.Dispose();
}
