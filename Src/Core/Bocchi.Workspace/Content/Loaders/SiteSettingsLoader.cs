using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.Workspace.Content.Loaders;

/// <summary>
/// 站点设置加载器。读取 <c>site/site.yaml</c>（必需）+ <c>site/navigation.yaml</c>（可选，若存在则覆盖
/// <c>site.yaml</c> 中的 <c>navigation</c>）。
/// </summary>
public static class SiteSettingsLoader
{
    public static LoadResult<SiteSettings> Load(
        ContentLocation siteLocation,
        string siteYaml,
        ContentLocation? navigationLocation,
        string? navigationYaml)
    {
        ArgumentNullException.ThrowIfNull(siteLocation);
        ArgumentNullException.ThrowIfNull(siteYaml);

        var errors = new List<ContentValidationError>();

        YamlMappingNode? mapping;
        try
        {
            mapping = YamlAccess.Parse(siteYaml);
        }
        catch (YamlException ex)
        {
            errors.Add(new ContentValidationError(
                siteLocation.RelativePath, ContentKind.SiteSettings, null,
                ContentErrorSeverity.Error, "SITE_INVALID_YAML",
                $"site.yaml 解析失败：{ex.Message}"));
            return LoadResult.Fail<SiteSettings>(errors);
        }

        if (mapping is null)
        {
            errors.Add(new ContentValidationError(
                siteLocation.RelativePath, ContentKind.SiteSettings, null,
                ContentErrorSeverity.Error, "SITE_EMPTY",
                "site.yaml 内容为空。"));
            return LoadResult.Fail<SiteSettings>(errors);
        }

        var title = YamlAccess.GetString(mapping, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(new ContentValidationError(
                siteLocation.RelativePath, ContentKind.SiteSettings, "title",
                ContentErrorSeverity.Error, "SITE_MISSING_TITLE",
                "site.yaml 必须包含 title。"));
        }

        var description = YamlAccess.GetString(mapping, "description");
        var language = YamlAccess.GetString(mapping, "language") ?? "zh-CN";
        var timeZone = YamlAccess.GetString(mapping, "timeZone") ?? "Asia/Shanghai";

        Uri? baseUrl = null;
        var baseUrlRaw = YamlAccess.GetString(mapping, "baseUrl");
        if (string.IsNullOrWhiteSpace(baseUrlRaw))
        {
            errors.Add(new ContentValidationError(
                siteLocation.RelativePath, ContentKind.SiteSettings, "baseUrl",
                ContentErrorSeverity.Error, "SITE_MISSING_BASEURL",
                "site.yaml 必须包含 baseUrl。"));
        }
        else if (!Uri.TryCreate(baseUrlRaw, UriKind.Absolute, out baseUrl))
        {
            errors.Add(new ContentValidationError(
                siteLocation.RelativePath, ContentKind.SiteSettings, "baseUrl",
                ContentErrorSeverity.Error, "SITE_INVALID_BASEURL",
                $"baseUrl '{baseUrlRaw}' 不是合法的绝对 URL。"));
        }

        var author = ParseAuthor(mapping);
        var social = ParseSocial(mapping);
        var navigation = ParseNavigationFromMapping(mapping);

        if (navigationYaml is not null && navigationLocation is not null)
        {
            var navResult = TryParseNavigationFile(navigationYaml, navigationLocation, errors);
            if (navResult is not null)
            {
                navigation = navResult;
            }
        }

        var defaultThemeId = YamlAccess.GetString(mapping, "defaultThemeId");
        var enableRss = YamlAccess.GetBool(mapping, "enableRss") ?? true;
        var enableSitemap = YamlAccess.GetBool(mapping, "enableSitemap") ?? true;
        var enableSearch = YamlAccess.GetBool(mapping, "enableSearch") ?? true;

        if (errors.Any(e => e.Severity == ContentErrorSeverity.Error))
        {
            return LoadResult.Fail<SiteSettings>(errors);
        }

        var settings = new SiteSettings
        {
            Title = title!,
            Description = description,
            Language = language,
            TimeZone = timeZone,
            BaseUrl = baseUrl!,
            Author = author,
            Social = social,
            Navigation = navigation,
            DefaultThemeId = defaultThemeId,
            EnableRss = enableRss,
            EnableSitemap = enableSitemap,
            EnableSearch = enableSearch,
        };

        return LoadResult.Ok<SiteSettings>(settings, errors);
    }

    private static AuthorInfo? ParseAuthor(YamlMappingNode root)
    {
        var node = YamlAccess.GetMapping(root, "author");
        if (node is null)
        {
            return null;
        }

        var name = YamlAccess.GetString(node, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var bio = YamlAccess.GetString(node, "bio");
        var email = YamlAccess.GetString(node, "email");
        var avatarPath = YamlAccess.GetString(node, "avatar");
        var avatar = string.IsNullOrWhiteSpace(avatarPath) ? null : new MediaReference(avatarPath!);
        return new AuthorInfo(name!, bio, email, avatar);
    }

    private static List<SocialLink> ParseSocial(YamlMappingNode root)
    {
        var seq = YamlAccess.GetSequence(root, "social");
        if (seq is null)
        {
            return [];
        }

        var list = new List<SocialLink>();
        foreach (var child in seq.Children)
        {
            if (child is YamlMappingNode m)
            {
                var platform = YamlAccess.GetString(m, "platform");
                var url = YamlAccess.GetString(m, "url");
                if (!string.IsNullOrWhiteSpace(platform) && !string.IsNullOrWhiteSpace(url))
                {
                    list.Add(new SocialLink(platform!, url!));
                }
            }
        }

        return list;
    }

    private static List<NavigationItem> ParseNavigationFromMapping(YamlMappingNode root)
    {
        var seq = YamlAccess.GetSequence(root, "navigation");
        return seq is null ? [] : ParseNavigationSequence(seq);
    }

    private static List<NavigationItem> ParseNavigationSequence(YamlSequenceNode seq)
    {
        var list = new List<NavigationItem>();
        foreach (var child in seq.Children)
        {
            if (child is YamlMappingNode m)
            {
                var t = YamlAccess.GetString(m, "title");
                var h = YamlAccess.GetString(m, "href");
                if (!string.IsNullOrWhiteSpace(t) && !string.IsNullOrWhiteSpace(h))
                {
                    list.Add(new NavigationItem(t!, h!));
                }
            }
        }

        return list;
    }

    private static List<NavigationItem>? TryParseNavigationFile(
        string yaml, ContentLocation location, List<ContentValidationError> errors)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count == 0)
            {
                return null;
            }

            return stream.Documents[0].RootNode switch
            {
                YamlSequenceNode s => ParseNavigationSequence(s),
                YamlMappingNode m when YamlAccess.GetSequence(m, "items") is { } itemsSeq
                    => ParseNavigationSequence(itemsSeq),
                _ => null,
            };
        }
        catch (YamlException ex)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.SiteSettings, null,
                ContentErrorSeverity.Error, "NAV_INVALID_YAML",
                $"navigation.yaml 解析失败：{ex.Message}"));
            return null;
        }
    }
}
