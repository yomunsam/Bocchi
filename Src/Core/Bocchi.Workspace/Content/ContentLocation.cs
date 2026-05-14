namespace Bocchi.Workspace.Content;

/// <summary>
/// 一份内容在内容空间中的位置寻址。
/// </summary>
/// <param name="ContentSpaceRoot">内容空间根目录绝对路径。</param>
/// <param name="RelativePath">相对内容空间根的路径，使用 <c>/</c> 分隔（与平台无关）。</param>
public sealed record ContentLocation(string ContentSpaceRoot, string RelativePath)
{
    /// <summary>源文件的绝对路径。</summary>
    public string AbsolutePath => Path.GetFullPath(Path.Combine(ContentSpaceRoot, RelativePath));

    /// <summary>对调试日志友好的展示文本。</summary>
    public override string ToString() => RelativePath;
}
