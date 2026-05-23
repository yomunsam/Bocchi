using System.Text.Json;
using System.Text.Json.Nodes;

using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Theme;
using Bocchi.Generator.ThemeInputs;
using Bocchi.Workspace;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>把内容图序列化为 Theme 输入 JSON，并写到 Sink（<see cref="ArtifactKind.ThemeInput"/>）。</summary>
public sealed class WriteThemeInputStage : IBuildStage
{
    private readonly ThemeInputWriter _writer;
    private readonly BocchiDataLayout _layout;

    public WriteThemeInputStage(ThemeInputWriter writer, BocchiDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(layout);
        _writer = writer;
        _layout = layout;
    }

    public string Name => nameof(WriteThemeInputStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Graph is null)
        {
            throw new BuildPipelineException($"{Name} 需要内容图。");
        }

        var themeId = session.GetItem<string>(BuildSessionKeys.ThemeId);
        if (string.IsNullOrWhiteSpace(themeId))
        {
            themeId = session.Graph.Site.Settings.DefaultThemeId;
        }

        if (string.IsNullOrWhiteSpace(themeId))
        {
            themeId = "unknown";
        }

        var loadedTheme = session.GetItem<LoadedTheme>(BuildSessionKeys.LoadedTheme);
        var bocchiVersion = session.GetItem<string>(BuildSessionKeys.BocchiVersion);
        var themeConfig = await ReadThemeConfigAsync(themeId, session.CancellationToken).ConfigureAwait(false);
        await ApplyConfigSchemaDefaultsAsync(loadedTheme, themeConfig, session.CancellationToken).ConfigureAwait(false);
        var pairs = _writer.Build(
            session.Graph,
            themeId,
            session.Options.Mode,
            session.Options.Environment,
            session.Options.IncludeDrafts,
            bocchiVersion,
            loadedTheme?.Manifest,
            themeConfig,
            session.Options.Localization);
        foreach (var (artifact, _) in pairs)
        {
            await ArtifactSinkHelper.WriteAsync(session, artifact).ConfigureAwait(false);
        }

        session.Log(Name, BuildLogLevel.Info, $"Theme 输入数据 {pairs.Count} 个文件已写入。");
        return true;
    }

    private async Task<JsonObject> ReadThemeConfigAsync(string themeId, CancellationToken cancellationToken)
    {
        var path = ResolveThemeConfigPath(themeId);
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        try
        {
            var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return node switch
            {
                null => new JsonObject(),
                JsonObject obj => obj,
                _ => throw new BuildPipelineException($"Theme 配置 '{path}' 必须是 JSON object。"),
            };
        }
        catch (JsonException ex)
        {
            throw new BuildPipelineException($"Theme 配置 '{path}' 不是合法 JSON。", ex);
        }
    }

    private string ResolveThemeConfigPath(string themeId)
    {
        if (themeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            themeId.Contains('/') ||
            themeId.Contains('\\') ||
            string.Equals(themeId, ".", StringComparison.Ordinal) ||
            string.Equals(themeId, "..", StringComparison.Ordinal))
        {
            throw new BuildPipelineException($"Theme id '{themeId}' 不能作为 Theme 配置文件名。");
        }

        return Path.Combine(_layout.ThemeConfigDirectory, themeId + ".json");
    }

    private static async Task ApplyConfigSchemaDefaultsAsync(
        LoadedTheme? loadedTheme,
        JsonObject themeConfig,
        CancellationToken cancellationToken)
    {
        if (loadedTheme is null)
        {
            return;
        }

        var schemaPath = Path.Combine(loadedTheme.ThemeRoot, "config-schema.json");
        if (!File.Exists(schemaPath))
        {
            return;
        }

        await using var stream = new FileStream(
            schemaPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        JsonNode? node;
        try
        {
            node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new BuildPipelineException($"Theme config-schema '{schemaPath}' 不是合法 JSON。", ex);
        }

        if (node?["groups"] is not JsonArray groups)
        {
            return;
        }

        foreach (var field in groups.OfType<JsonObject>()
                     .Select(group => group["fields"])
                     .OfType<JsonArray>()
                     .SelectMany(fields => fields.OfType<JsonObject>()))
        {
            var key = field["key"]?.GetValue<string>();
            var defaultValue = field["default"];
            if (string.IsNullOrWhiteSpace(key) || defaultValue is null)
            {
                continue;
            }

            ApplyDefaultValue(themeConfig, key, defaultValue);
        }
    }

    private static void ApplyDefaultValue(JsonObject root, string dottedKey, JsonNode defaultValue)
    {
        var segments = dottedKey.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return;
        }

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }

            current = child;
        }

        var leaf = segments[^1];
        if (current[leaf] is JsonObject existingObject && defaultValue is JsonObject defaultObject)
        {
            MergeDefaultObject(existingObject, defaultObject);
            return;
        }

        current.TryAdd(leaf, defaultValue.DeepClone());
    }

    /// <summary>递归补齐 object 默认值，让多语言配置可以只覆盖部分语言，其余语言继续使用 Theme 默认值。</summary>
    private static void MergeDefaultObject(JsonObject target, JsonObject defaults)
    {
        foreach (var (key, defaultValue) in defaults)
        {
            if (target[key] is JsonObject targetChild && defaultValue is JsonObject defaultChild)
            {
                MergeDefaultObject(targetChild, defaultChild);
                continue;
            }

            target.TryAdd(key, defaultValue?.DeepClone());
        }
    }
}
