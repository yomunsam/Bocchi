namespace Bocchi.Generator.Theme;

/// <summary>Theme 来源类型；Dashboard 和 Build log 用它说明当前 Theme Root 来自哪里。</summary>
public enum ThemeSourceKind
{
    /// <summary>随 Bocchi 发布的内置参考 Theme。</summary>
    BuiltIn,

    /// <summary>已经安装在 <c>&lt;data&gt;/themes/&lt;theme-id&gt;/</c> 下的 Theme。</summary>
    Installed,

    /// <summary>开发期从 <c>dev-links.json</c> 指向的外部 Theme Root。</summary>
    DevLink,

    /// <summary>Zip 上传检查阶段的候选 Theme；进入安装前不会成为可用 Theme。</summary>
    PackageCandidate,
}
