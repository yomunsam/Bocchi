namespace Bocchi.ContentModel.Tests;

public sealed class ContentModelContractTests
{
    [Fact]
    public void ContentStatus_HasMvpMembers()
    {
        Enum.IsDefined(ContentStatus.Draft).Should().BeTrue();
        Enum.IsDefined(ContentStatus.Published).Should().BeTrue();
        Enum.IsDefined(ContentStatus.Archived).Should().BeTrue();
    }

    [Fact]
    public void ContentKind_CoversArchitectureMvp()
    {
        var expected = new[]
        {
            ContentKind.Post,
            ContentKind.Page,
            ContentKind.Work,
            ContentKind.Note,
            ContentKind.FriendLink,
            ContentKind.SiteSettings,
            ContentKind.Photo,
        };

        Enum.GetValues<ContentKind>().Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Post_CanBeConstructedWithRequiredFields()
    {
        var post = new Post
        {
            Slug = "hello-bocchi",
            Title = "Hello Bocchi",
        };

        post.Slug.Should().Be("hello-bocchi");
        post.Title.Should().Be("Hello Bocchi");
        post.Status.Should().Be(ContentStatus.Draft);
        post.Tags.Should().BeEmpty();
    }

    [Fact]
    public void SiteSettings_HasSensibleDefaults()
    {
        var settings = new SiteSettings
        {
            Title = "Bocchi",
            BaseUrl = new Uri("https://example.invalid/"),
        };

        settings.Language.Should().Be("zh-CN");
        settings.EnableRss.Should().BeTrue();
        settings.EnableSitemap.Should().BeTrue();
        settings.EnableSearch.Should().BeTrue();
    }
}
