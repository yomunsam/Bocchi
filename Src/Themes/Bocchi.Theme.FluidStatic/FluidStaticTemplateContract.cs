namespace Bocchi.Theme.FluidStatic;

/// <summary>定义 Fluid Static v1 必须由每个 Theme 自己提供的模板文件。</summary>
internal static class FluidStaticTemplateContract
{
    /// <summary>标准路由渲染所需的完整模板清单，路径相对 Theme 的 <c>templates</c> 目录。</summary>
    public static IReadOnlyList<string> RequiredTemplates { get; } =
    [
        "layouts/base.liquid",
        "pages/index.liquid",
        "pages/posts.liquid",
        "pages/works.liquid",
        "pages/notes.liquid",
        "pages/friends.liquid",
        "pages/article.liquid",
        "pages/standalone-page.liquid",
        "pages/404.liquid",
    ];

    /// <summary>确认当前 Theme 自己拥有全部必需模板，禁止从其他 Theme 静默补齐。</summary>
    public static void EnsureRequiredTemplates(string themeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRoot);

        foreach (var relativePath in RequiredTemplates)
        {
            var templatePath = Path.Combine(
                themeRoot,
                "templates",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(templatePath))
            {
                throw new FluidStaticException(
                    $"Fluid Static Theme 缺少必需模板 'templates/{relativePath}'。");
            }
        }
    }
}
