namespace Bocchi.Workspace;

/// <summary>
/// 内容 workspace 加载器。负责打开指定路径上的 <see cref="IWorkspace"/>。
/// </summary>
/// <remarks>
/// 该契约只作用于用户内容目录，不负责创建 DataRoot 下的数据库、缓存或输出目录。
/// </remarks>
public interface IWorkspaceLoader
{
    /// <summary>
    /// 在指定根目录上打开一个内容 workspace。如果尚未初始化，实现可选择抛出异常或返回未初始化状态的实例。
    /// </summary>
    /// <param name="rootPath">内容 workspace 根目录的绝对路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<IWorkspace> OpenAsync(string rootPath, CancellationToken cancellationToken = default);
}
