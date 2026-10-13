using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Publishing;
using Bocchi.Workspace;

namespace Bocchi.HomeServer.Tests;

public sealed class LocalDirectoryPublisherTests
{
    [Fact]
    public async Task PublishAsync_MirrorsFiles_AndRemovesStaleOnes()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-local-pub-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(Path.GetTempPath(), "bocchi-local-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new BocchiDataLayout(dataRoot);
            Directory.CreateDirectory(layout.PublicOutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "<html>one</html>");
            Directory.CreateDirectory(Path.Combine(layout.PublicOutputDirectory, "posts"));
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "posts", "a.html"), "a");

            var publisher = new LocalDirectoryPublisher(layout);
            var request = CreateRequest(layout, target, allowUnmarked: false);
            var result = await publisher.PublishAsync(request, TestContext.Current.CancellationToken);

            result.RemoteUrl.Should().NotBeNullOrWhiteSpace();
            File.ReadAllText(Path.Combine(target, "index.html")).Should().Contain("one");
            File.Exists(Path.Combine(target, "posts", "a.html")).Should().BeTrue();
            File.Exists(Path.Combine(target, LocalDirectoryPublisher.MarkerFileName)).Should().BeTrue();

            // stale file from a previous manual copy should be removed on re-publish
            await File.WriteAllTextAsync(Path.Combine(target, "stale.txt"), "gone");
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "<html>two</html>");
            File.Delete(Path.Combine(layout.PublicOutputDirectory, "posts", "a.html"));

            request = CreateRequest(layout, target, allowUnmarked: false);
            await publisher.PublishAsync(request, TestContext.Current.CancellationToken);

            File.ReadAllText(Path.Combine(target, "index.html")).Should().Contain("two");
            File.Exists(Path.Combine(target, "posts", "a.html")).Should().BeFalse();
            File.Exists(Path.Combine(target, "stale.txt")).Should().BeFalse();
        }
        finally
        {
            TryDelete(dataRoot);
            TryDelete(target);
        }
    }

    [Fact]
    public async Task PublishAsync_RejectsUnmarkedNonEmptyTarget_UnlessAllowed()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-local-pub-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(Path.GetTempPath(), "bocchi-local-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new BocchiDataLayout(dataRoot);
            Directory.CreateDirectory(layout.PublicOutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "x");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "preexisting.txt"), "keep?");

            var publisher = new LocalDirectoryPublisher(layout);
            var act = () => publisher.PublishAsync(CreateRequest(layout, target, allowUnmarked: false), TestContext.Current.CancellationToken);
            await act.Should().ThrowAsync<PublishTargetException>().WithMessage("*无标记*");

            await publisher.PublishAsync(CreateRequest(layout, target, allowUnmarked: true), TestContext.Current.CancellationToken);
            File.Exists(Path.Combine(target, "preexisting.txt")).Should().BeFalse();
            File.Exists(Path.Combine(target, "index.html")).Should().BeTrue();
        }
        finally
        {
            TryDelete(dataRoot);
            TryDelete(target);
        }
    }

    [Fact]
    public async Task PublishAsync_RejectsRelativeOrDataRootPaths()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-local-pub-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new BocchiDataLayout(dataRoot);
            Directory.CreateDirectory(layout.PublicOutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "x");
            var publisher = new LocalDirectoryPublisher(layout);

            var relative = () => publisher.PublishAsync(
                CreateRequest(layout, "relative/out", allowUnmarked: true),
                TestContext.Current.CancellationToken);
            await relative.Should().ThrowAsync<PublishTargetException>().WithMessage("*绝对*");

            var intoWorkspace = () => publisher.PublishAsync(
                CreateRequest(layout, layout.WorkspaceRoot, allowUnmarked: true),
                TestContext.Current.CancellationToken);
            await intoWorkspace.Should().ThrowAsync<PublishTargetException>().WithMessage("*DataRoot*");
        }
        finally
        {
            TryDelete(dataRoot);
        }
    }

    private static PublishTargetRequest CreateRequest(BocchiDataLayout layout, string target, bool allowUnmarked)
    {
        var configuration = new LocalDirectoryPublishConfiguration
        {
            TargetDirectory = target,
            AllowUnmarkedTarget = allowUnmarked,
        }.Normalize();
        var output = StaticOutputEnumerator.Enumerate(layout.PublicOutputDirectory);
        return new PublishTargetRequest
        {
            Plan = new PublishPlanRecord
            {
                Id = 1,
                DisplayName = "Local",
                Channel = PublishPlanService.LocalDirectoryChannel,
                ConfigurationJson = configuration.ToJson(),
            },
            ConfigurationJson = configuration.ToJson(),
            CredentialJson = null,
            Output = output,
            BuildResult = new BuildResult
            {
                SessionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Mode = BuildMode.FullBuild,
                Status = BuildStatus.Succeeded,
                StartedAt = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero),
                FinishedAt = new DateTimeOffset(2026, 10, 13, 0, 0, 1, TimeSpan.Zero),
                Fingerprint = new BuildFingerprint("test-fp"),
                Logs = [],
                Artifacts = [],
                BuildRunId = 1,
            },
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup races on CI
        }
    }
}
