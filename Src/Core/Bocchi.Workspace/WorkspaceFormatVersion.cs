namespace Bocchi.Workspace;

/// <summary>
/// workspace 格式版本。写在 <c>site/site.yaml</c> 顶层 <c>formatVersion</c>。
/// 缺省（老目录无字段）视为 <see cref="Current"/>；读到更高版本时拒绝写入与会改状态的构建。
/// </summary>
public static class WorkspaceFormatVersion
{
    /// <summary>当前程序认识的 workspace 格式版本。</summary>
    public const int Current = 1;

    /// <summary>缺省（无字段）时按此版本解释，便于 rc 前无实例过渡。</summary>
    public const int DefaultWhenMissing = 1;
}
