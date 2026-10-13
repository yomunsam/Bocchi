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
            var result = await publisher.PublishAsync(CreateRequest(layout, target, allowUnmarked: false), TestContext.Current.CancellationToken);

            result.RemoteUrl.Should().NotBeNullOrWhiteSpace();
            File.ReadAllText(Path.Combine(target, "index.html")).Should().Contain("one");
            File.Exists(Path.Combine(target, "posts", "a.html")).Should().BeTrue();
            File.Exists(Path.Combine(target, LocalDirectoryPublisher.MarkerFileName)).Should().BeTrue();
            LocalDirectoryPublisher.TryReadValidMarker(Path.Combine(target, LocalDirectoryPublisher.MarkerFileName), out var marker)
                .Should().BeTrue();
            marker!.FileCount.Should().Be(2);

            await File.WriteAllTextAsync(Path.Combine(target, "stale.txt"), "gone");
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "<html>two</html>");
            File.Delete(Path.Combine(layout.PublicOutputDirectory, "posts", "a.html"));

            await publisher.PublishAsync(CreateRequest(layout, target, allowUnmarked: false), TestContext.Current.CancellationToken);

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
    public async Task PublishAsync_RejectsEntireDataRootTree()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-local-pub-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new BocchiDataLayout(dataRoot);
            Directory.CreateDirectory(layout.PublicOutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "x");
            var publisher = new LocalDirectoryPublisher(layout);

            foreach (var forbidden in new[]
                     {
                         layout.DataRoot,
                         layout.BackupsDirectory,
                         layout.OutputDirectory,
                         layout.PublicOutputDirectory,
                         layout.CacheDirectory,
                         layout.LogsDirectory,
                         layout.WorkspaceRoot,
                         Path.Combine(layout.BackupsDirectory, "nested", "deeper"),
                         Path.Combine(layout.OutputDirectory, "extra"),
                     })
            {
                var act = () => publisher.PublishAsync(CreateRequest(layout, forbidden, allowUnmarked: true), TestContext.Current.CancellationToken);
                await act.Should().ThrowAsync<PublishTargetException>().WithMessage("*DataRoot*");
            }
        }
        finally
        {
            TryDelete(dataRoot);
        }
    }

    [Fact]
    public async Task PublishAsync_RejectsSymlinkIntoDataRoot()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-local-pub-" + Guid.NewGuid().ToString("N"));
        var linkParent = Path.Combine(Path.GetTempPath(), "bocchi-local-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new BocchiDataLayout(dataRoot);
            Directory.CreateDirectory(layout.PublicOutputDirectory);
            Directory.CreateDirectory(layout.BackupsDirectory);
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "x");
            Directory.CreateDirectory(linkParent);
            var linkPath = Path.Combine(linkParent, "into-backups");
            Directory.CreateSymbolicLink(linkPath, layout.BackupsDirectory);

            var publisher = new LocalDirectoryPublisher(layout);
            var act = () => publisher.PublishAsync(CreateRequest(layout, linkPath, allowUnmarked: true), TestContext.Current.CancellationToken);
            await act.Should().ThrowAsync<PublishTargetException>().WithMessage("*DataRoot*");
        }
        finally
        {
            TryDelete(dataRoot);
            TryDelete(linkParent);
        }
    }

    [Fact]
    public async Task PublishAsync_RejectsCorruptMarker_UnlessAllowUnmarked()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-local-pub-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(Path.GetTempPath(), "bocchi-local-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new BocchiDataLayout(dataRoot);
            Directory.CreateDirectory(layout.PublicOutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "x");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "keep.txt"), "y");
            await File.WriteAllTextAsync(Path.Combine(target, LocalDirectoryPublisher.MarkerFileName), "{ not-json");

            var publisher = new LocalDirectoryPublisher(layout);
            var denied = () => publisher.PublishAsync(CreateRequest(layout, target, allowUnmarked: false), TestContext.Current.CancellationToken);
            await denied.Should().ThrowAsync<PublishTargetException>().WithMessage("*标记*");

            await publisher.PublishAsync(CreateRequest(layout, target, allowUnmarked: true), TestContext.Current.CancellationToken);
            File.Exists(Path.Combine(target, "keep.txt")).Should().BeFalse();
            LocalDirectoryPublisher.TryReadValidMarker(Path.Combine(target, LocalDirectoryPublisher.MarkerFileName), out _)
                .Should().BeTrue();
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
            await act.Should().ThrowAsync<PublishTargetException>().WithMessage("*标记*");

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

    [Fact]
    public async Task PublishAsync_SerializesConcurrentMirrors()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-local-pub-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(Path.GetTempPath(), "bocchi-local-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new BocchiDataLayout(dataRoot);
            Directory.CreateDirectory(layout.PublicOutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(layout.PublicOutputDirectory, "index.html"), "shared");
            var publisher = new LocalDirectoryPublisher(layout);
            var request = CreateRequest(layout, target, allowUnmarked: true);

            var tasks = Enumerable.Range(0, 4)
                .Select(_ => publisher.PublishAsync(request, TestContext.Current.CancellationToken))
                .ToArray();
            var results = await Task.WhenAll(tasks);
            results.Should().AllSatisfy(r => r.RemoteUrl.Should().NotBeNullOrWhiteSpace());
            File.ReadAllText(Path.Combine(target, "index.html")).Should().Be("shared");
            LocalDirectoryPublisher.TryReadValidMarker(Path.Combine(target, LocalDirectoryPublisher.MarkerFileName), out _)
                .Should().BeTrue();
        }
        finally
        {
            TryDelete(dataRoot);
            TryDelete(target);
        }
    }

    [Fact]
    public void ResolveFinalPath_FollowsSymlinkDirectory()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var real = Path.Combine(Path.GetTempPath(), "bocchi-resolve-real-" + Guid.NewGuid().ToString("N"));
        var linkParent = Path.Combine(Path.GetTempPath(), "bocchi-resolve-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(real);
            Directory.CreateDirectory(linkParent);
            var link = Path.Combine(linkParent, "alias");
            Directory.CreateSymbolicLink(link, real);
            LocalDirectoryPublisher.ResolveFinalPath(link).Should().Be(Path.GetFullPath(real));
        }
        finally
        {
            TryDelete(real);
            TryDelete(linkParent);
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
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore cleanup races on CI
        }
    }
}
