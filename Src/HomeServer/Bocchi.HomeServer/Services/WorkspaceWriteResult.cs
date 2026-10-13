using Bocchi.Workspace;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// workspace 权威文件的写入结果。失败时不改文件，也不改数据库。
/// <see cref="Message"/> 给日志和构建原因；后台界面用 <see cref="UiKey"/> 取国际化文案。
/// </summary>
public sealed record WorkspaceWriteResult(bool Succeeded, string? Code, string? Message)
{
    /// <summary>分类文件解析或 schema 不通过。</summary>
    public const string CategoriesInvalidCode = "CATEGORIES_YAML_INVALID";

    /// <summary>本地化文件解析或 schema 不通过。</summary>
    public const string LocalizationInvalidCode = "LOCALIZATION_YAML_INVALID";

    /// <summary>写入完成。</summary>
    public static WorkspaceWriteResult Ok { get; } = new(true, null, null);

    /// <summary>拒绝这次写入。</summary>
    public static WorkspaceWriteResult Fail(string code, string message) => new(false, code, message);

    /// <summary>Dashboard 文案 key。未知错误码回退到通用拒绝文案。</summary>
    public string UiKey => Code switch
    {
        WorkspaceFormatVersion.UnsupportedCode => "workspace.formatVersion.unsupported",
        WorkspaceFormatVersion.InvalidCode => "workspace.formatVersion.invalid",
        CategoriesInvalidCode => "workspace.categories.invalidFile",
        LocalizationInvalidCode => "workspace.localization.invalidFile",
        _ => "workspace.writeRefused",
    };
}
