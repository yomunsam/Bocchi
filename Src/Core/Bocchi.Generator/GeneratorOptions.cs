namespace Bocchi.Generator;

/// <summary>Bocchi Generator 的可调选项，绑定到 <c>appsettings.json</c> 的 <c>Bocchi:Generator</c> 节。</summary>
public sealed class GeneratorOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "Bocchi:Generator";

    /// <summary>是否在启动时迁移 SQLite schema 到 v3。默认 <c>true</c>。</summary>
    public bool RunSchemaMigrationOnStartup { get; set; } = true;

    /// <summary>单次构建中调用 Theme runner 的最大耗时（分钟）。</summary>
    public int ThemeRunTimeoutMinutes { get; set; } = 10;
}
