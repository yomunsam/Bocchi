namespace Bocchi.GeneratorContract;

/// <summary>
/// Theme 输入数据 JSON 的 <c>$schema</c> URI 常量。详见 <c>Docs/Milestones/M3/M3.md §3.5</c>。
/// </summary>
/// <remarks>
/// 这些 URI 是稳定标识符，**不要求**可被解析为可下载文档；Theme 端只用作"我读到了哪个版本的合同"
/// 的对账标记，未来若发布人类可读 JSON Schema，也会落到对应路径。
/// </remarks>
public static class ContractSchemaIds
{
    private const string Base = "https://bocchi.local/schema/v1/";

    public const string Site = Base + "site.json";
    public const string Navigation = Base + "navigation.json";
    public const string Posts = Base + "posts.json";
    public const string Pages = Base + "pages.json";
    public const string Works = Base + "works.json";
    public const string Notes = Base + "notes.json";
    public const string Friends = Base + "friends.json";
    public const string Photos = Base + "photos.json";
    public const string ThemeConfig = Base + "theme-config.json";
    public const string BuildContext = Base + "build-context.json";
}