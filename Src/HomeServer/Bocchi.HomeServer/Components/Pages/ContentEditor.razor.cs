using System.Text.Json;

using Bocchi.ContentModel;
using Bocchi.HomeServer.Services.Ai;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bocchi.HomeServer.Components.Pages;

/// <summary>ContentEditor 的交互逻辑扩展；主 .razor 文件保留页面结构和通用编辑状态。</summary>
public partial class ContentEditor
{
    /// <summary>AI slug 生成的最多重试次数；每次都会经过 Home Server 规范化与查重。</summary>
    private const int MaxAiSlugAttempts = 3;

    /// <summary>AI slug 会话的 system prompt，要求模型只返回路径标识，避免解释性文本污染结果。</summary>
    private const string AiSlugSystemPrompt =
        "You generate short, semantic URL path slugs for blog posts and pages. Return exactly one slug and nothing else. Prefer clear English words. If the title or summary is written in CJK or another non-Latin language, translate the core meaning into concise English instead of returning the original script. Use lowercase ASCII letters and digits separated by single hyphens only. Do not use slashes, punctuation, spaces, quotes, Markdown, JSON, or explanatory text.";

    /// <summary>AI slug 按钮文案会随执行状态变化，给等待中的本地模型调用明确反馈。</summary>
    private string AiSlugButtonLabel => _aiSlugBusy ? "正在生成路径标识" : "AI生成路径标识";

    /// <summary>AI slug 按钮加载态使用旋转动画；普通态保持轻量图标按钮。</summary>
    private string AiSlugButtonClass => _aiSlugBusy
        ? "bocchi-editor-slug-control__ai bocchi-editor-slug-control__ai--busy"
        : "bocchi-editor-slug-control__ai";

    /// <summary>处理标题输入；未发布且本页未手动改过 slug 时，路径标识会继续跟随标题变化。</summary>
    private void OnTitleInput(ChangeEventArgs args)
    {
        _title = args.Value?.ToString() ?? string.Empty;
        _saved = false;
        if (CanFollowTitleSlug)
        {
            _slug = ContentSlug.Normalize(_title);
        }
    }

    /// <summary>处理 slug 输入；本页第一次手动修改后，标题变化不再自动覆盖用户选择。</summary>
    private void OnSlugInput(ChangeEventArgs args)
    {
        _slugTouchedInSession = true;
        _slug = ContentSlug.Normalize(args.Value?.ToString());
        _saved = false;
    }

    /// <summary>打开 AI slug 生成确认弹窗；不可用状态下按钮不会出现，这里仍保留保护。</summary>
    private void OpenAiSlugConfirm()
    {
        if (!CanUseAiSlug)
        {
            return;
        }

        _showAiSlugFailure = false;
        _showAiSlugConfirm = true;
    }

    /// <summary>关闭 AI slug 生成确认弹窗。</summary>
    private void CancelAiSlugConfirm()
    {
        _showAiSlugConfirm = false;
    }

    /// <summary>关闭 AI slug 生成失败弹窗。</summary>
    private void CloseAiSlugFailure()
    {
        _showAiSlugFailure = false;
    }

    /// <summary>在一个浏览器 AI 会话内最多尝试三次，并让 Home Server 对每次结果做规范化与查重。</summary>
    private async Task GenerateAiSlugAsync()
    {
        var kind = CurrentKind();
        if (!CanUseAiSlug ||
            _file is null ||
            kind is null ||
            kind.Value is not (ContentKind.Post or ContentKind.Page))
        {
            return;
        }

        _showAiSlugConfirm = false;
        _showAiSlugFailure = false;
        _aiSlugBusy = true;
        _saveMessage = null;
        await InvokeAsync(StateHasChanged);
        var unavailableSlugs = new List<string>();
        AiSessionReference? session = null;
        try
        {
            session = await Ai.CreateSessionAsync(new AiSessionRequest
            {
                SystemPrompt = AiSlugSystemPrompt,
                ExpectedInputLanguages = ["en", "ja"],
                ExpectedOutputLanguages = ["en"],
                Temperature = 0.2,
                TopK = 4,
            });

            for (var attempt = 0; attempt < MaxAiSlugAttempts; attempt++)
            {
                var prompt = BuildAiSlugPrompt(unavailableSlugs);
                var response = await Ai.PromptSessionAsync(session.SessionId, prompt);
                var candidate = ExtractAiSlugCandidate(response.Text);
                if (!IsAsciiSlugCandidate(candidate))
                {
                    unavailableSlugs.Add(string.IsNullOrWhiteSpace(candidate)
                        ? "empty slug: return a meaningful lowercase ASCII slug"
                        : $"{candidate}: use lowercase ASCII English words only");
                    continue;
                }

                var validation = Editor.ValidateUrlSlug(kind.Value, _file.RelativePath, candidate);
                if (validation.IsAvailable)
                {
                    _slug = validation.Slug;
                    _slugTouchedInSession = true;
                    _saved = false;
                    _saveMessage = "已生成路径标识。";
                    return;
                }

                unavailableSlugs.Add(string.IsNullOrWhiteSpace(validation.Slug)
                    ? "empty slug: Home Server rejected it"
                    : $"{validation.Slug}: rejected by Home Server");
            }

            _showAiSlugFailure = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or JSDisconnectedException or IOException or UnauthorizedAccessException)
        {
            _showAiSlugFailure = true;
        }
        finally
        {
            if (session is not null)
            {
                await DestroyAiSessionBestEffortAsync(session.SessionId);
            }

            _aiSlugBusy = false;
            StateHasChanged();
        }
    }

    /// <summary>保存前让 Home Server 统一规范化与查重，避免前端或 AI 直接决定最终 Path。</summary>
    private bool TryPrepareSlugForSave(out string? error)
    {
        error = null;
        var kind = CurrentKind();
        if (_file is null || kind is null || kind.Value is not (ContentKind.Post or ContentKind.Page))
        {
            return true;
        }

        var fallback = SlugFromPath(_file.RelativePath);
        var validation = Editor.ValidateUrlSlug(
            kind.Value,
            _file.RelativePath,
            string.IsNullOrWhiteSpace(_slug) ? fallback : _slug);
        if (!validation.IsAvailable)
        {
            error = validation.Reason ?? "路径标识不可用。";
            return false;
        }

        _slug = validation.Slug;
        return true;
    }

    /// <summary>生成写入 YAML 的 slug；Post/Page 使用 Unicode 内容 slug，其它类型保持既有路径 fallback。</summary>
    private string SlugForYaml()
    {
        var fallback = SlugFromPath(_file?.RelativePath ?? string.Empty);
        if (!IsSlugManagedContent)
        {
            return string.IsNullOrWhiteSpace(_slug) ? fallback : _slug.Trim();
        }

        var normalized = ContentSlug.Normalize(string.IsNullOrWhiteSpace(_slug) ? fallback : _slug);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    /// <summary>构造一次 AI slug prompt；重试时会把不可用结果明确反馈给模型。</summary>
    private string BuildAiSlugPrompt(List<string> unavailableSlugs)
    {
        var unavailableText = unavailableSlugs.Count == 0
            ? "None."
            : string.Join("\n", unavailableSlugs.Select(x => "- " + x));

        return $$"""
            Generate one URL path slug for the current content.
            Return only the slug itself.

            Content type: {{AiContentKindLabel}}
            Title: {{_title}}
            Summary: {{_summary}}

            Goal:
            - Prefer a meaningful English or ASCII slug so the URL is short, search-friendly, and compatible when shared on third-party platforms.
            - Translate the main meaning instead of copying CJK text directly.
            - Use lowercase ASCII letters and digits only, separated by single hyphens.
            - Keep it concise, ideally 2 to 6 words.
            - Avoid vague slugs such as post, page, update, article, note, new-post, or untitled.
            - Do not include slashes, punctuation, spaces, quotes, Markdown, JSON, or explanations.

            Slugs rejected by Home Server:
            {{unavailableText}}
            """;
    }

    /// <summary>AI 生成链路额外要求 ASCII slug；默认无 AI 的标题跟随逻辑仍允许 Unicode。</summary>
    private static bool IsAsciiSlugCandidate(string slug)
        => !string.IsNullOrWhiteSpace(slug) &&
            slug.All(static ch => ch is '-' || char.IsAsciiLetterOrDigit(ch));

    /// <summary>AI prompt 使用英文内容类型，避免把 Dashboard UI 语言混入模型任务。</summary>
    private string AiContentKindLabel => CurrentKind() switch
    {
        ContentKind.Page => "page",
        ContentKind.Work => "work",
        _ => "post",
    };

    /// <summary>从模型输出中提取候选 slug；支持纯文本、单行 <c>slug:</c> 和简单 JSON 返回。</summary>
    private static string ExtractAiSlugCandidate(string text)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (TryReadSlugFromJson(trimmed, out var jsonSlug))
        {
            return ContentSlug.Normalize(jsonSlug);
        }

        foreach (var line in SplitLines(trimmed))
        {
            var candidate = CleanAiSlugLine(line);
            var normalized = ContentSlug.Normalize(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return ContentSlug.Normalize(trimmed);
    }

    /// <summary>尝试从 JSON 对象的 slug 字段读取模型结果。</summary>
    private static bool TryReadSlugFromJson(string text, out string slug)
    {
        slug = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("slug", out var slugElement) &&
                slugElement.ValueKind == JsonValueKind.String)
            {
                slug = slugElement.GetString() ?? string.Empty;
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>清理模型可能包上的标签、引号或 Markdown code fence。</summary>
    private static string CleanAiSlugLine(string line)
    {
        var candidate = line.Trim().Trim('`', '"', '\'', '“', '”', '‘', '’');
        const string slugPrefix = "slug:";
        return candidate.StartsWith(slugPrefix, StringComparison.OrdinalIgnoreCase)
            ? candidate[slugPrefix.Length..].Trim()
            : candidate;
    }

    /// <summary>尽力释放浏览器 AI 会话；销毁失败不应覆盖前面的生成结果或错误提示。</summary>
    private async Task DestroyAiSessionBestEffortAsync(string sessionId)
    {
        try
        {
            await Ai.DestroySessionAsync(sessionId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or JSDisconnectedException)
        {
        }
    }
}
