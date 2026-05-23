using System.Globalization;
using System.Text.Encodings.Web;

using Fluid;
using Fluid.Values;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme 的 Fluid 模板执行器。</summary>
internal static class DefaultStaticFluidRenderer
{
    /// <summary>FluidParser 可跨渲染复用；每次渲染仍会创建新的 TemplateContext。</summary>
    private static readonly FluidParser Parser = new();

    /// <summary>默认模板选项，注册 Bocchi 约定的安全正文 HTML filter。</summary>
    private static readonly TemplateOptions Options = CreateOptions();

    /// <summary>渲染一个页面模板，并套用全局 layout。</summary>
    public static async Task<string> RenderPageAsync(
        string themeRoot,
        string pageTemplateName,
        IReadOnlyDictionary<string, object?> model,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTemplateName);
        ArgumentNullException.ThrowIfNull(model);

        var pageRelativePath = $"pages/{pageTemplateName}.liquid";
        var pageSource = await ReadTemplateAsync(themeRoot, pageRelativePath, cancellationToken).ConfigureAwait(false);
        var body = await RenderTemplateAsync(pageRelativePath, pageSource, model).ConfigureAwait(false);

        var layoutModel = new Dictionary<string, object?>(model, StringComparer.Ordinal)
        {
            ["content"] = body,
        };
        var layoutSource = await ReadTemplateAsync(themeRoot, "layouts/base.liquid", cancellationToken).ConfigureAwait(false);
        return await RenderTemplateAsync("layouts/base.liquid", layoutSource, layoutModel).ConfigureAwait(false);
    }

    /// <summary>检查页面模板是否存在于可覆盖 Theme 目录或内置默认资源中。</summary>
    public static async Task<bool> PageTemplateExistsAsync(
        string themeRoot,
        string pageTemplateName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTemplateName);

        var relativePath = $"pages/{pageTemplateName}.liquid";
        var diskPath = Path.Combine(themeRoot, "templates", relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(diskPath))
        {
            return true;
        }

        return await DefaultStaticThemeDefinition.TryReadTemplateAsync(relativePath, cancellationToken)
            .ConfigureAwait(false) is not null;
    }

    /// <summary>读取工作区模板；缺失时回退到内置默认模板，保证旧工作区也能构建。</summary>
    private static async Task<string> ReadTemplateAsync(string themeRoot, string relativePath, CancellationToken cancellationToken)
    {
        var diskPath = Path.Combine(themeRoot, "templates", relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(diskPath))
        {
            return await File.ReadAllTextAsync(diskPath, cancellationToken).ConfigureAwait(false);
        }

        return await DefaultStaticThemeDefinition.TryReadTemplateAsync(relativePath, cancellationToken).ConfigureAwait(false)
            ?? throw new DefaultStaticThemeException($"默认 Theme 缺少模板 '{relativePath}'。");
    }

    /// <summary>解析并执行单个 Fluid 模板，输出时启用 HTML encoder。</summary>
    private static async Task<string> RenderTemplateAsync(
        string relativePath,
        string source,
        IReadOnlyDictionary<string, object?> model)
    {
        if (!Parser.TryParse(source, out var template, out var error))
        {
            throw new DefaultStaticThemeException($"Theme 模板 '{relativePath}' 解析失败：{error}");
        }

        var context = new TemplateContext(model, Options, allowModelMembers: true)
        {
            // 固定 Culture，避免不同服务器区域设置导致默认 Theme HTML 不稳定。
            CultureInfo = CultureInfo.InvariantCulture,
        };
        try
        {
            return await template.RenderAsync(context, HtmlEncoder.Default, isolateContext: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not DefaultStaticThemeException)
        {
            throw new DefaultStaticThemeException($"Theme 模板 '{relativePath}' 渲染失败：{ex.Message}", ex);
        }
    }

    /// <summary>创建默认 Fluid 执行选项。</summary>
    private static TemplateOptions CreateOptions()
    {
        var options = new TemplateOptions
        {
            CultureInfo = CultureInfo.InvariantCulture,
        };
        options.Filters.AddFilter("html", static (input, _, _) =>
            new ValueTask<FluidValue>(new StringValue(input.ToStringValue(), encode: false)));
        return options;
    }
}
