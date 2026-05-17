namespace Bocchi.Workspace.Content;

/// <summary>
/// 一份内容在 workspace 中的位置寻址。
/// </summary>
/// <param name="WorkspaceRoot">内容 workspace 根目录绝对路径。</param>
/// <param name="RelativePath">相对内容 workspace 根的路径，使用 <c>/</c> 分隔（与平台无关）。</param>
public sealed record ContentLocation(string WorkspaceRoot, string RelativePath)
{
    /// <summary>源文件的绝对路径。</summary>
    public string AbsolutePath => Path.GetFullPath(Path.Combine(WorkspaceRoot, RelativePath));

    /// <summary>对调试日志友好的展示文本。</summary>
    public override string ToString() => RelativePath;
}
