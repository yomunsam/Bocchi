namespace Bocchi.Workspace;

/// <summary>
/// Bocchi 工作区的统一抽象。M1 阶段只定义契约，具体实现由 M2 落地。
/// </summary>
public interface IWorkspace
{
    /// <summary>工作区目录布局。</summary>
    WorkspaceLayout Layout { get; }

    /// <summary>检查工作区是否已经初始化（即关键目录与状态数据库存在）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已初始化返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    ValueTask<bool> IsInitializedAsync(CancellationToken cancellationToken = default);
}