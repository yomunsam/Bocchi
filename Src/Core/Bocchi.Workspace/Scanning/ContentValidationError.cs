using Bocchi.ContentModel;

namespace Bocchi.Workspace.Scanning;

/// <summary>校验错误的严重级别。</summary>
public enum ContentErrorSeverity
{
    /// <summary>提示信息（如孤儿媒体），不阻断扫描。</summary>
    Info = 0,

    /// <summary>警告（如可疑派生产物扩展名）。</summary>
    Warning = 1,

    /// <summary>错误，对应内容会被视为无效；扫描继续，但该内容不会进入索引。</summary>
    Error = 2,
}

/// <summary>
/// 扫描期间产生的一条校验错误。错误聚合输出，扫描不会因单条错误而中止。
/// </summary>
/// <param name="RelativePath">相对内容空间根的源文件路径，<c>/</c> 分隔。</param>
/// <param name="Kind">所属内容类型；如果错误产生于目录布局阶段（无法识别 kind），可能为 <c>null</c>。</param>
/// <param name="Field">出错字段名（frontmatter 字段或路径字段），可空。</param>
/// <param name="Severity">严重级别。</param>
/// <param name="Code">错误代码（机读）。</param>
/// <param name="Message">人读消息。</param>
public sealed record ContentValidationError(
    string RelativePath,
    ContentKind? Kind,
    string? Field,
    ContentErrorSeverity Severity,
    string Code,
    string Message);