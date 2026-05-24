using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Git;
using Bocchi.HomeServer.Services.Publishing;
using Bocchi.Workspace;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Tests;

/// <summary>验证发布执行服务的构建、输出校验、publisher 调度和运行历史持久化。</summary>
public sealed class PublishExecutionServiceTests
{
    /// <summary>构建成功后发布静态输出，并记录发布运行。</summary>
    [Fact]
    public async Task PublishAsync_SucceededBuild_PublishesAndPersistsRun()
    {
        await using var context = await CreateContextAsync();
        var plan = await context.PlanService.SaveAsync(CreateGitHubPlanInput("plain-token"));
        context.WritePublicFile("index.html", "<h1>Hello</h1>");
        context.Runner.Result = CreateBuildResult(BuildStatus.Succeeded);

        var result = await context.Execution.PublishAsync(plan.Id);

        result.Status.Should().Be(PublishRunStatus.Succeeded);
        result.RemoteCommitSha.Should().Be("remote-sha");
        context.Publisher.Calls.Should().Be(1);
        var run = await context.Db.PublishRuns.SingleAsync();
        run.Status.Should().Be(PublishRunStatus.Succeeded);
        run.BuildRunId.Should().Be(42);
        run.ArtifactCount.Should().Be(1);
        plan.ProtectedCredentialJson.Should().NotContain("plain-token");
    }

    /// <summary>构建因同指纹跳过时，只要本地输出仍存在，就继续执行远端发布。</summary>
    [Fact]
    public async Task PublishAsync_SkippedBuildWithExistingOutput_StillPublishes()
    {
        await using var context = await CreateContextAsync();
        var plan = await context.PlanService.SaveAsync(CreateGitHubPlanInput("plain-token"));
        context.WritePublicFile("index.html", "<h1>Hello</h1>");
        context.Runner.Result = CreateBuildResult(BuildStatus.Skipped);

        var result = await context.Execution.PublishAsync(plan.Id);

        result.Status.Should().Be(PublishRunStatus.Succeeded);
        context.Publisher.Calls.Should().Be(1);
    }

    /// <summary>构建成功但 output/public 为空时不触发远端 publisher。</summary>
    [Fact]
    public async Task PublishAsync_EmptyOutput_FailsBeforeRemotePublisher()
    {
        await using var context = await CreateContextAsync();
        var plan = await context.PlanService.SaveAsync(CreateGitHubPlanInput("plain-token"));
        context.Runner.Result = CreateBuildResult(BuildStatus.Succeeded);

        var result = await context.Execution.PublishAsync(plan.Id);

        result.Status.Should().Be(PublishRunStatus.Failed);
        result.ErrorMessage.Should().Contain("静态输出目录");
        context.Publisher.Calls.Should().Be(0);
        var run = await context.Db.PublishRuns.SingleAsync();
        run.Status.Should().Be(PublishRunStatus.Failed);
    }

    /// <summary>创建带受保护 token 的 GitHub Pages 方案输入。</summary>
    private static PublishPlanSaveInput CreateGitHubPlanInput(string token)
    {
        var configuration = new GitHubPagesPublishConfiguration
        {
            Owner = "owner",
            Repository = "site",
            Branch = "gh-pages",
            EnsurePagesSource = false,
        };
        var credential = new GitHubPagesPublishCredential { AccessToken = token };
        return new PublishPlanSaveInput(
            null,
            "GitHub Pages",
            PublishPlanService.GitHubPagesChannel,
            configuration.ToJson(),
            credential.ToJson(),
            SetAsDefault: true);
    }

    /// <summary>创建稳定的构建结果快照。</summary>
    private static BuildResult CreateBuildResult(BuildStatus status)
        => new()
        {
            SessionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Mode = BuildMode.FullBuild,
            Status = status,
            StartedAt = new DateTimeOffset(2026, 5, 24, 0, 0, 0, TimeSpan.Zero),
            FinishedAt = new DateTimeOffset(2026, 5, 24, 0, 0, 1, TimeSpan.Zero),
            Fingerprint = new BuildFingerprint("abcdef1234567890"),
            Logs = [],
            Artifacts = [],
            BuildRunId = 42,
        };

    /// <summary>创建内存 SQLite、临时 DataRoot 和 fake publisher 组成的测试上下文。</summary>
    private static async Task<TestContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new BocchiDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-publish-execution-tests", Guid.NewGuid().ToString("N"));
        var layout = new BocchiDataLayout(dataRoot);
        var keyDir = Path.Combine(Path.GetTempPath(), "bocchi-test-keys", Guid.NewGuid().ToString("N"));
        var protection = DataProtectionProvider.Create(new DirectoryInfo(keyDir));
        var planService = new PublishPlanService(db, protection, TimeProvider.System);
        var connectionService = new GitProviderConnectionService(db, protection, TimeProvider.System);
        var runner = new FakeBuildRunner();
        var publisher = new FakePublisher();
        var execution = new PublishExecutionService(
            db,
            planService,
            connectionService,
            runner,
            layout,
            [publisher],
            TimeProvider.System);
        return new TestContext(connection, db, layout, planService, runner, publisher, execution, keyDir);
    }

    private sealed class TestContext : IAsyncDisposable
    {
        /// <summary>构造发布执行测试上下文。</summary>
        public TestContext(
            SqliteConnection connection,
            BocchiDbContext db,
            BocchiDataLayout layout,
            PublishPlanService planService,
            FakeBuildRunner runner,
            FakePublisher publisher,
            PublishExecutionService execution,
            string keyDirectory)
        {
            Connection = connection;
            Db = db;
            Layout = layout;
            PlanService = planService;
            Runner = runner;
            Publisher = publisher;
            Execution = execution;
            KeyDirectory = keyDirectory;
        }

        /// <summary>内存 SQLite 连接，生命周期绑定 DbContext。</summary>
        public SqliteConnection Connection { get; }

        /// <summary>测试用 Home Server DbContext。</summary>
        public BocchiDbContext Db { get; }

        /// <summary>测试用 DataRoot 布局。</summary>
        public BocchiDataLayout Layout { get; }

        /// <summary>测试中的发布方案服务。</summary>
        public PublishPlanService PlanService { get; }

        /// <summary>可控构建结果的 fake runner。</summary>
        public FakeBuildRunner Runner { get; }

        /// <summary>记录调用次数的 fake publisher。</summary>
        public FakePublisher Publisher { get; }

        /// <summary>被测发布执行服务。</summary>
        public PublishExecutionService Execution { get; }

        /// <summary>Data Protection key 临时目录。</summary>
        public string KeyDirectory { get; }

        /// <summary>写入 output/public 下的测试文件。</summary>
        public void WritePublicFile(string relativePath, string content)
        {
            var path = Path.Combine(Layout.PublicOutputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        /// <summary>释放数据库连接并删除临时目录。</summary>
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
            DeleteBestEffort(Layout.DataRoot);
            DeleteBestEffort(KeyDirectory);
        }

        /// <summary>尽量删除测试临时目录。</summary>
        private static void DeleteBestEffort(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    /// <summary>测试用构建 runner，返回预设的 BuildResult。</summary>
    private sealed class FakeBuildRunner : IStaticSiteBuildRunner
    {
        /// <summary>下一次构建调用要返回的结果。</summary>
        public BuildResult Result { get; set; } = CreateBuildResult(BuildStatus.Succeeded);

        /// <summary>返回预设构建结果。</summary>
        public Task<BuildResult> RunPublishBuildAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }

    /// <summary>测试用发布目标，记录调用并返回固定远端结果。</summary>
    private sealed class FakePublisher : IPublishTargetPublisher
    {
        /// <inheritdoc />
        public string Channel => PublishPlanService.GitHubPagesChannel;

        /// <summary>publisher 被调用的次数。</summary>
        public int Calls { get; private set; }

        /// <summary>记录一次发布调用并返回固定远端结果。</summary>
        public Task<PublishTargetResult> PublishAsync(PublishTargetRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new PublishTargetResult
            {
                RemoteCommitSha = "remote-sha",
                RemoteUrl = "https://owner.github.io/site/",
            });
        }
    }
}
