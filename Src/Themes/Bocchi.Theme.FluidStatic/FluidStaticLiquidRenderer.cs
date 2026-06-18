using System.Globalization;
using System.Text.Encodings.Web;

using Fluid;
using Fluid.Values;

namespace Bocchi.Theme.FluidStatic;

/// <summary>默认静态 Theme 的 Fluid 模板执行器。</summary>
internal static class FluidStaticLiquidRenderer
{
    /// <summary>FluidParser 可跨渲染复用；每次渲染仍会创建新的 TemplateContext。</summary>
    private static readonly FluidParser Parser = new();

    /// <summary>渲染一个页面模板，并套用全局 layout。</summary>
    public static async Task<string> RenderPageAsync(
        string themeRoot,
        string pageTemplateName,
        IReadOnlyDictionary<string, object?> model,
        FluidStaticTextResolver text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTemplateName);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);

        var options = CreateOptions(text);
        var pageRelativePath = $"pages/{pageTemplateName}.liquid";
        var pageSource = await ReadTemplateAsync(themeRoot, pageRelativePath, cancellationToken).ConfigureAwait(false);
        var body = await RenderTemplateAsync(pageRelativePath, pageSource, model, options).ConfigureAwait(false);

        var layoutModel = new Dictionary<string, object?>(model, StringComparer.Ordinal)
        {
            ["content"] = body,
        };
        var layoutSource = await ReadTemplateAsync(themeRoot, "layouts/base.liquid", cancellationToken).ConfigureAwait(false);
        return await RenderTemplateAsync("layouts/base.liquid", layoutSource, layoutModel, options).ConfigureAwait(false);
    }

    /// <summary>检查页面模板是否存在于当前 Theme 目录，不读取其他 Theme 的资源。</summary>
    public static Task<bool> PageTemplateExistsAsync(
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
            return Task.FromResult(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    /// <summary>读取当前 Theme 的模板；缺失时明确报告 Theme 包不完整。</summary>
    private static async Task<string> ReadTemplateAsync(string themeRoot, string relativePath, CancellationToken cancellationToken)
    {
        var diskPath = Path.Combine(themeRoot, "templates", relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(diskPath))
        {
            throw new FluidStaticException(
                $"Fluid Static Theme 缺少必需模板 'templates/{relativePath}'。");
        }

        return await File.ReadAllTextAsync(diskPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>解析并执行单个 Fluid 模板，输出时启用 HTML encoder。</summary>
    private static async Task<string> RenderTemplateAsync(
        string relativePath,
        string source,
        IReadOnlyDictionary<string, object?> model,
        TemplateOptions options)
    {
        if (!Parser.TryParse(source, out var template, out var error))
        {
            throw new FluidStaticException($"Theme 模板 '{relativePath}' 解析失败：{error}");
        }

        var context = new TemplateContext(model, options, allowModelMembers: true)
        {
            // 固定 Culture，避免不同服务器区域设置导致默认 Theme HTML 不稳定。
            CultureInfo = CultureInfo.InvariantCulture,
        };
        try
        {
            return await template.RenderAsync(context, HtmlEncoder.Default, isolateContext: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not FluidStaticException)
        {
            throw new FluidStaticException($"Theme 模板 '{relativePath}' 渲染失败：{ex.Message}", ex);
        }
    }

    /// <summary>创建默认 Fluid 执行选项。</summary>
    private static TemplateOptions CreateOptions(FluidStaticTextResolver text)
    {
        var options = new TemplateOptions
        {
            CultureInfo = CultureInfo.InvariantCulture,
        };
        options.Filters.AddFilter("html", static (input, _, _) =>
            new ValueTask<FluidValue>(new StringValue(input.ToStringValue(), encode: false)));
        options.Filters.AddFilter("t", (input, _, _) =>
            new ValueTask<FluidValue>(new StringValue(text.Get(input.ToStringValue()))));
        return options;
    }
}
