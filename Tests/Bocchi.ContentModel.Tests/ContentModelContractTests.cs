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
        settings.DefaultTitle.Should().BeNull();
        settings.CopyrightNotice.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("!!!", "")]
    [InlineData("我的 第一篇文章！", "我的-第一篇文章")]
    [InlineData("吾輩は猫である", "吾輩は猫である")]
    [InlineData("Hello, Bocchi!", "hello-bocchi")]
    [InlineData("設計 2026 / Devlog", "設計-2026-devlog")]
    [InlineData("  Hello---Bocchi___LIVE  ", "hello-bocchi-live")]
    [InlineData("孤独🚀ROCK 2026", "孤独-rock-2026")]
    [InlineData("Ｃ＃ 入門 １２３", "c-入門-123")]
    [InlineData("Café Été", "café-été")]
    public void ContentSlug_NormalizesUnicodePathSegments(string? value, string expected)
    {
        ContentSlug.Normalize(value).Should().Be(expected);
    }

    [Fact]
    public void ContentSlug_KeepsLongValidInputWithoutTruncation()
    {
        ContentSlug.Normalize(new string('A', 300)).Should().Be(new string('a', 300));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("!!!", "")]
    [InlineData("我的 第一篇文章！", "")]
    [InlineData("吾輩は猫である", "")]
    [InlineData("Hello, Bocchi!", "hello-bocchi")]
    [InlineData("設計 2026 / Devlog", "2026-devlog")]
    [InlineData("  Hello---Bocchi___LIVE  ", "hello-bocchi-live")]
    [InlineData("孤独🚀ROCK 2026", "rock-2026")]
    [InlineData("Café Été", "cafe-ete")]
    [InlineData("C#/.NET Tips", "c-net-tips")]
    public void CategorySlug_NormalizesAsciiUrlSegments(string? value, string expected)
    {
        CategorySlug.Normalize(value).Should().Be(expected);
    }

    [Fact]
    public void CategorySlug_KeepsLongValidAsciiInputWithoutTruncation()
    {
        CategorySlug.Normalize(new string('A', 300)).Should().Be(new string('a', 300));
    }

    [Fact]
    public void ContentSlugAndCategorySlug_HaveDifferentUnicodeContracts()
    {
        const string value = "設計 2026 / Devlog";

        ContentSlug.Normalize(value).Should().Be("設計-2026-devlog");
        CategorySlug.Normalize(value).Should().Be("2026-devlog");
    }
}
