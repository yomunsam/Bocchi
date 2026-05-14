namespace Bocchi.Generator.Exceptions;

/// <summary>
/// 构建 <see cref="ContentGraph.ContentGraph"/> 时遇到的不可恢复错误：
/// slug 冲突、媒体引用缺失、字段约束违反等。详见 <c>Docs/Milestones/M3/M3.md §3.3</c>。
/// </summary>
public sealed class ContentGraphException : GeneratorException
{
    public ContentGraphException(string message)
        : base(message)
    {
    }

    public ContentGraphException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}