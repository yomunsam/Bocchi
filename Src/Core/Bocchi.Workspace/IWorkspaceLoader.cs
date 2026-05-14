namespace Bocchi.Workspace;

/// <summary>
/// 工作区加载器。负责打开或初始化指定路径上的 <see cref="IWorkspace"/>。
/// </summary>
/// <remarks>
/// M1 仅定义契约。M2 阶段实现具体的目录扫描、创建 <c>.bocchi/</c> 子目录、初始化 SQLite 等逻辑。
/// </remarks>
public interface IWorkspaceLoader
{
    /// <summary>
    /// 在指定根目录上打开一个工作区。如果工作区尚未初始化，实现可选择抛出异常或返回未初始化状态的实例。
    /// </summary>
    /// <param name="rootPath">工作区根目录的绝对路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<IWorkspace> OpenAsync(string rootPath, CancellationToken cancellationToken = default);
}