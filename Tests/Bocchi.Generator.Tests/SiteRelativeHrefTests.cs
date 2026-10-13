using Bocchi.Generator.SiteArtifacts;

namespace Bocchi.Generator.Tests;

public sealed class SiteRelativeHrefTests
{
    [Theory]
    [InlineData("/posts/hello/")]
    [InlineData("/")]
    [InlineData("./local")]
    [InlineData("../up")]
    [InlineData("docs/guide")]
    public void IsAllowed_AcceptsSiteRelative(string url)
        => SiteRelativeHref.IsAllowed(url).Should().BeTrue();

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("http://evil.example/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("//evil.example/x")]
    [InlineData("")]
    [InlineData("mailto:a@b.c")]
    public void IsAllowed_RejectsAbsoluteAndDangerous(string url)
        => SiteRelativeHref.IsAllowed(url).Should().BeFalse();
}
