namespace Bocchi.Workspace;

/// <summary>
/// 用户内容 workspace 的统一抽象。它只代表可迁移、可独立 Git 化的创作内容区。
/// </summary>
public interface IWorkspace
{
    /// <summary>内容 workspace 目录布局。</summary>
    WorkspaceLayout Layout { get; }

    /// <summary>检查内容 workspace 是否已经初始化（即关键目录与 site.yaml 存在）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已初始化返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    ValueTask<bool> IsInitializedAsync(CancellationToken cancellationToken = default);
}
