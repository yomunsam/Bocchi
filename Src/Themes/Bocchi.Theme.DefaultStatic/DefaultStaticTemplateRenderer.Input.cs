using System.Text.Json;

using Bocchi.GeneratorContract;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme renderer 的 Theme Contract 输入读取与 schema 文本格式解析。</summary>
public sealed partial class DefaultStaticTemplateRenderer
{
    /// <summary>读取所有 M5 默认 Theme 需要的 Theme Contract 输入。</summary>
    private static async Task<ThemeInputSet> ReadInputAsync(string inputDirectory, CancellationToken cancellationToken)
    {
        return new ThemeInputSet(
            await ReadDataAsync(inputDirectory, "theme-context.json", cancellationToken).ConfigureAwait(false),
            ReadNavigationItems(await ReadDataAsync(inputDirectory, "navigation.json", cancellationToken).ConfigureAwait(false)),
            await ReadArrayAsync(inputDirectory, "post-categories.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "posts.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "pages.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "works.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "notes.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "friends.json", cancellationToken).ConfigureAwait(false));
    }

    /// <summary>读取单个 envelope 的 <c>data</c> 对象，并验证 Theme Contract 版本。</summary>
    private static async Task<JsonElement> ReadDataAsync(string inputDirectory, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(inputDirectory, fileName);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var version = root.GetProperty("contractVersion").GetString();
            if (!string.Equals(version, ThemeContractVersion.Current, StringComparison.Ordinal))
            {
                throw new DefaultStaticThemeException($"Theme 输入 '{fileName}' contractVersion='{version}'，当前只支持 '{ThemeContractVersion.Current}'。");
            }

            return root.GetProperty("data").Clone();
        }
        catch (JsonException ex)
        {
            throw new DefaultStaticThemeException($"Theme 输入 '{fileName}' 不是合法 JSON。", ex);
        }
    }

    /// <summary>读取 envelope 的 <c>data</c> 数组。</summary>
    private static async Task<JsonElement[]> ReadArrayAsync(string inputDirectory, string fileName, CancellationToken cancellationToken)
    {
        var data = await ReadDataAsync(inputDirectory, fileName, cancellationToken).ConfigureAwait(false);
        if (data.ValueKind != JsonValueKind.Array)
        {
            throw new DefaultStaticThemeException($"Theme 输入 '{fileName}' 的 data 必须是 array。");
        }

        return data.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    /// <summary>读取 Theme schema 中声明的可控文本格式；缺失或未知格式都会回退为 plain。</summary>
    private static async Task<IReadOnlyDictionary<string, string>> ReadConfigTextFormatsAsync(
        string themeRoot,
        JsonElement context,
        CancellationToken cancellationToken)
    {
        var schema = await TryReadConfigSchemaAsync(themeRoot, context, cancellationToken).ConfigureAwait(false);
        if (schema is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(schema);
        if (!document.RootElement.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups.EnumerateArray().Where(group => group.ValueKind == JsonValueKind.Object))
        {
            if (!group.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var field in fields.EnumerateArray().Where(field => field.ValueKind == JsonValueKind.Object))
            {
                var key = GetString(field, "key");
                var type = GetString(field, "type");
                var format = DefaultStaticInlineTextRenderer.NormalizeFormat(GetString(field, "textFormat"));
                if (!string.IsNullOrWhiteSpace(key) &&
                    IsTextFormatEligibleField(type) &&
                    DefaultStaticInlineTextRenderer.IsInlineColorFormat(format))
                {
                    result[key.Trim()] = format;
                }
            }
        }

        return result;
    }

    /// <summary>读取当前 Theme 的 schema；内置默认 Theme 在运行实例缺文件时可回退到 embedded resource。</summary>
    private static async Task<string?> TryReadConfigSchemaAsync(
        string themeRoot,
        JsonElement context,
        CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(themeRoot, "config-schema.json");
        if (File.Exists(schemaPath))
        {
            return await File.ReadAllTextAsync(schemaPath, cancellationToken).ConfigureAwait(false);
        }

        var themeId = TryGetPath(context, ["theme", "id"])?.GetString();
        return string.Equals(themeId, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal)
            ? await DefaultStaticThemeResources.TryReadTextAsync("config-schema.json", cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <summary>判断字段类型是否允许声明 inline 文本格式。</summary>
    private static bool IsTextFormatEligibleField(string type)
        => string.Equals(type, "string", StringComparison.Ordinal)
            || string.Equals(type, "localizedText", StringComparison.Ordinal);

    /// <summary>读取 navigation.json 的 items 数组；无菜单时返回空数组。</summary>
    private static JsonElement[] ReadNavigationItems(JsonElement navigationData)
    {
        if (navigationData.ValueKind != JsonValueKind.Object ||
            !navigationData.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray().Select(item => item.Clone()).ToArray();
    }
}
