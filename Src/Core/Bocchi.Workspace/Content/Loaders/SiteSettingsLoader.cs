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

        var defaultTitle = YamlAccess.GetString(mapping, "defaultTitle");
        var description = YamlAccess.GetString(mapping, "description");
        var language = YamlAccess.GetString(mapping, "language") ?? "zh-CN";
        var timeZone = YamlAccess.GetString(mapping, "timeZone") ?? "Asia/Shanghai";

        Uri? baseUrl = null;
        var baseUrlRaw = YamlAccess.GetString(mapping, "baseUrl");
        if (string.IsNullOrWhiteSpace(baseUrlRaw))
        {
            baseUrl = new Uri("http://localhost/");
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
        var copyrightNotice = YamlAccess.GetString(mapping, "copyright");

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
        var feedItemCount = YamlAccess.GetInt(mapping, "feedItemCount") ?? 20;
        if (feedItemCount < 1)
        {
            feedItemCount = 1;
        }

        var robots = ParseRobots(mapping);

        if (errors.Any(e => e.Severity == ContentErrorSeverity.Error))
        {
            return LoadResult.Fail<SiteSettings>(errors);
        }

        var settings = new SiteSettings
        {
            Title = title!,
            DefaultTitle = defaultTitle,
            Description = description,
            Language = language,
            TimeZone = timeZone,
            BaseUrl = baseUrl!,
            CopyrightNotice = copyrightNotice,
            Author = author,
            Social = social,
            Navigation = navigation,
            DefaultThemeId = defaultThemeId,
            EnableRss = enableRss,
            EnableSitemap = enableSitemap,
            EnableSearch = enableSearch,
            FeedItemCount = feedItemCount,
            Robots = robots,
        };

        return LoadResult.Ok<SiteSettings>(settings, errors);
    }

    private static RobotsPolicy ParseRobots(YamlMappingNode root)
    {
        var node = YamlAccess.GetMapping(root, "robots");
        if (node is null)
        {
            return new RobotsPolicy();
        }

        var allow = YamlAccess.GetStringList(node, "allow");
        var disallow = YamlAccess.GetStringList(node, "disallow");
        return new RobotsPolicy
        {
            Allow = allow.Count == 0 ? new RobotsPolicy().Allow : allow,
            Disallow = disallow,
        };
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
        => ParseNavigationSequence(seq, depth: 0, path: "nav");

    private static List<NavigationItem> ParseNavigationSequence(YamlSequenceNode seq, int depth, string path)
    {
        if (depth >= CategoryDepthLimit)
        {
            return [];
        }

        var list = new List<NavigationItem>();
        for (var i = 0; i < seq.Children.Count; i++)
        {
            var child = seq.Children[i];
            if (child is YamlMappingNode m)
            {
                var target = ParseNavigationTarget(m);
                if (target is null)
                {
                    continue;
                }

                var id = YamlAccess.GetString(m, "id");
                var label = YamlAccess.GetString(m, "label");
                var children = YamlAccess.GetSequence(m, "children") is { } childSeq
                    ? ParseNavigationSequence(childSeq, depth + 1, $"{path}-{i}")
                    : [];
                list.Add(new NavigationItem
                {
                    Id = string.IsNullOrWhiteSpace(id) ? $"{path}-{i}" : id.Trim(),
                    Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                    Target = target,
                    Children = children,
                });
            }
        }

        return list;
    }

    private const int CategoryDepthLimit = 5;

    private static NavigationTarget? ParseNavigationTarget(YamlMappingNode item)
    {
        var target = YamlAccess.GetMapping(item, "target");
        if (target is null)
        {
            return null;
        }

        var type = YamlAccess.GetString(target, "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var value = YamlAccess.GetString(target, "value");
        return new NavigationTarget
        {
            Type = type.Trim(),
            Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim(),
        };
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
