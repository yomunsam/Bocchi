using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.Workspace.Content.Loaders;

/// <summary>
/// 友链加载器。读取单文件 <c>friends/friends.yaml</c>，输出 <see cref="FriendLink"/> 列表。
/// </summary>
public static class FriendLinksLoader
{
    public static LoadResult<IReadOnlyList<FriendLink>> Load(ContentLocation location, string yamlContent)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(yamlContent);

        var errors = new List<ContentValidationError>();
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            return LoadResult.Ok<IReadOnlyList<FriendLink>>([], errors);
        }

        YamlNode? root;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yamlContent);
            stream.Load(reader);
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
        }
        catch (YamlException ex)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.FriendLink, null,
                ContentErrorSeverity.Error, "FRIENDS_INVALID_YAML",
                $"friends.yaml 解析失败：{ex.Message}"));
            return LoadResult.Fail<IReadOnlyList<FriendLink>>(errors);
        }

        // 兼容两种顶层形态：直接 sequence，或 mapping 下含 friends: [...]
        YamlSequenceNode? seq = root switch
        {
            YamlSequenceNode s => s,
            YamlMappingNode m => YamlAccess.GetSequence(m, "friends"),
            _ => null,
        };

        if (seq is null)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.FriendLink, null,
                ContentErrorSeverity.Error, "FRIENDS_INVALID_SHAPE",
                "friends.yaml 顶层必须是数组，或包含 friends 数组的对象。"));
            return LoadResult.Fail<IReadOnlyList<FriendLink>>(errors);
        }

        var list = new List<FriendLink>();
        for (var i = 0; i < seq.Children.Count; i++)
        {
            if (seq.Children[i] is not YamlMappingNode m)
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, ContentKind.FriendLink, $"[{i}]",
                    ContentErrorSeverity.Error, "FRIEND_INVALID_ITEM",
                    "友链项必须是对象。"));
                continue;
            }

            var name = YamlAccess.GetString(m, "name");
            var url = YamlAccess.GetString(m, "url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, ContentKind.FriendLink, $"[{i}]",
                    ContentErrorSeverity.Error, "FRIEND_MISSING_FIELD",
                    "友链必须同时提供 name 与 url。"));
                continue;
            }

            var avatarPath = YamlAccess.GetString(m, "avatar");
            var description = YamlAccess.GetString(m, "description");
            var tags = YamlAccess.GetStringList(m, "tags");
            var status = PostLoader.ParseStatus(
                YamlAccess.GetString(m, "status"), errors,
                new ContentLocation(location.ContentSpaceRoot, $"{location.RelativePath}#[{i}]"),
                ContentKind.FriendLink);
            // FriendLink 默认 Published；ParseStatus 缺省返回 Draft，这里做覆盖
            if (string.IsNullOrWhiteSpace(YamlAccess.GetString(m, "status")))
            {
                status = ContentStatus.Published;
            }

            var order = YamlAccess.GetInt(m, "order") ?? 0;

            list.Add(new FriendLink
            {
                Name = name!,
                Url = url!,
                Avatar = string.IsNullOrWhiteSpace(avatarPath) ? null : new MediaReference(avatarPath!),
                Description = description,
                Tags = tags,
                Status = status,
                Order = order,
            });
        }

        return LoadResult.Ok<IReadOnlyList<FriendLink>>(list, errors);
    }
}
