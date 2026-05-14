namespace Bocchi.Workspace.Exceptions;

/// <summary>
/// 内容解析阶段抛出的"硬"错误。"软"错误（个别字段缺失/非法）通过
/// <c>ContentValidationError</c> 聚合上报，而不是抛异常。
/// </summary>
public sealed class ContentParseException : Exception
{
    public ContentParseException(string message)
        : base(message)
    {
    }

    public ContentParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}