namespace Bocchi.Generator.Theme;

/// <summary>Theme 开发期来源的运行选项，绑定到 <c>Bocchi:Themes</c>。</summary>
public sealed class ThemeDevelopmentOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "Bocchi:Themes";

    /// <summary>
    /// 是否允许 Dev Link。为空时 Development 默认启用，其他环境默认禁用。
    /// </summary>
    public bool? AllowDevLinks { get; set; }

    /// <summary>当前宿主环境名；由 HomeServer 在启动时写入，测试可直接覆盖。</summary>
    public string EnvironmentName { get; set; } = "Production";

    /// <summary>计算后的 Dev Link 开关。</summary>
    public bool AreDevLinksEnabled
        => AllowDevLinks ?? string.Equals(EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);
}
