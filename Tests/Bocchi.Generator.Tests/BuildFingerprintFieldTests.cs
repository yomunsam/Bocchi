using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.Generator.Tests;

/// <summary>验证 theme-input 全量指纹会随标题、标签、摘要、封面等输出相关字段变化。</summary>
public sealed class BuildFingerprintFieldTests
{
    [Fact]
    public async Task ChangingPostTitle_ChangesFingerprint()
    {
        await AssertFingerprintChangesWhenAsync(postPath =>
        {
            var text = File.ReadAllText(postPath);
            File.WriteAllText(postPath, text.Replace("title: Hello", "title: Hello Renamed", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task ChangingPostTags_ChangesFingerprint()
    {
        await AssertFingerprintChangesWhenAsync(postPath =>
        {
            var text = File.ReadAllText(postPath);
            File.WriteAllText(postPath, text.Replace("tags: [a, b]", "tags: [a, b, c]", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task ChangingPostSummary_ChangesFingerprint()
    {
        await AssertFingerprintChangesWhenAsync(postPath =>
        {
            var text = File.ReadAllText(postPath);
            File.WriteAllText(postPath, text.Replace(
                "tags: [a, b]\n---\n",
                "tags: [a, b]\nsummary: A short summary\n---\n",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task ChangingPostCover_ChangesFingerprint()
    {
        await AssertFingerprintChangesWhenAsync(postPath =>
        {
            var text = File.ReadAllText(postPath);
            File.WriteAllText(postPath, text.Replace(
                "tags: [a, b]\n---\n",
                "tags: [a, b]\ncover: assets/c.jpg\n---\n",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task ForceRebuild_DisablesShortCircuit()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var first = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            null,
            "0.0.0-test",
            TestContext.Current.CancellationToken);
        first.Status.Should().Be(BuildStatus.Succeeded);

        var forced = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, DisableUpToDateShortCircuit = true },
            new FileSystemBuildSink(fixture.Layout),
            null,
            "0.0.0-test",
            TestContext.Current.CancellationToken);

        forced.Status.Should().Be(BuildStatus.Succeeded);
        forced.Fingerprint!.Value.Should().Be(first.Fingerprint!.Value);
    }

    private static async Task AssertFingerprintChangesWhenAsync(Action<string> mutatePost)
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var postPath = Path.Combine(fixture.Layout.Workspace.PostsDirectory, "2025", "hello", "index.md");

        var first = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            null,
            "0.0.0-test",
            TestContext.Current.CancellationToken);
        first.Status.Should().Be(BuildStatus.Succeeded);

        mutatePost(postPath);

        var second = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            null,
            "0.0.0-test",
            TestContext.Current.CancellationToken);

        second.Status.Should().Be(BuildStatus.Succeeded);
        second.Fingerprint!.Value.Should().NotBe(first.Fingerprint!.Value);
    }
}
