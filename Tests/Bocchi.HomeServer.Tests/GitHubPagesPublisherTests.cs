using System.Net;
using System.Text;
using System.Text.Json;

using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Publishing;

namespace Bocchi.HomeServer.Tests;

/// <summary>验证 GitHub Pages publisher 的 Git Database API 调用形状和脱敏行为。</summary>
public sealed class GitHubPagesPublisherTests
{
    /// <summary>已有 branch 时创建完整 tree、带 parent commit，并 fast-forward 更新 ref。</summary>
    [Fact]
    public async Task PublishAsync_ExistingBranch_CreatesExactTreeAndUpdatesRef()
    {
        using var output = new TempOutput();
        output.WriteText("index.html", "<h1>Hello</h1>");
        output.WriteBytes("assets/logo.bin", [0, 1, 2, 3, 255]);
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, """{"object":{"sha":"parent-sha"}}"""),
            Json(HttpStatusCode.Created, """{"sha":"blob-logo"}"""),
            Json(HttpStatusCode.Created, """{"sha":"blob-index"}"""),
            Json(HttpStatusCode.Created, """{"sha":"tree-sha"}"""),
            Json(HttpStatusCode.Created, """{"sha":"commit-sha"}"""),
            Json(HttpStatusCode.OK, """{"object":{"sha":"commit-sha"}}"""),
            Json(HttpStatusCode.OK, """{"html_url":"https://owner.github.io/site/","source":{"branch":"gh-pages","path":"/"}}"""));
        var publisher = CreatePublisher(handler);

        var result = await publisher.PublishAsync(CreateRequest(output.Root, ensurePagesSource: true), default);

        result.RemoteCommitSha.Should().Be("commit-sha");
        result.RemoteUrl.Should().Be("https://owner.github.io/site/");
        handler.Requests.Select(x => x.Method.Method).Should().Equal("GET", "POST", "POST", "POST", "POST", "PATCH", "GET");
        var treeBody = JsonDocument.Parse(handler.Requests[3].Body!);
        treeBody.RootElement.TryGetProperty("base_tree", out _).Should().BeFalse();
        treeBody.RootElement.GetProperty("tree").EnumerateArray()
            .Select(x => x.GetProperty("path").GetString())
            .Should().Equal("assets/logo.bin", "index.html");
        var commitBody = JsonDocument.Parse(handler.Requests[4].Body!);
        commitBody.RootElement.GetProperty("parents").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("parent-sha");
        handler.Requests[5].Body.Should().Contain("\"force\":false");
    }

    /// <summary>branch 缺失时创建 root commit 和新 ref。</summary>
    [Fact]
    public async Task PublishAsync_MissingBranch_CreatesRootCommitAndReference()
    {
        using var output = new TempOutput();
        output.WriteText("index.html", "<h1>Hello</h1>");
        var handler = new RecordingHandler(
            Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
            Json(HttpStatusCode.Created, """{"sha":"blob-index"}"""),
            Json(HttpStatusCode.Created, """{"sha":"tree-sha"}"""),
            Json(HttpStatusCode.Created, """{"sha":"commit-sha"}"""),
            Json(HttpStatusCode.Created, """{"ref":"refs/heads/gh-pages","object":{"sha":"commit-sha"}}"""));
        var publisher = CreatePublisher(handler);

        var result = await publisher.PublishAsync(CreateRequest(output.Root, ensurePagesSource: false), default);

        result.RemoteCommitSha.Should().Be("commit-sha");
        handler.Requests.Select(x => x.Method.Method).Should().Equal("GET", "POST", "POST", "POST", "POST");
        var commitBody = JsonDocument.Parse(handler.Requests[3].Body!);
        commitBody.RootElement.GetProperty("parents").EnumerateArray().Should().BeEmpty();
        handler.Requests[4].Body.Should().Contain("\"ref\":\"refs/heads/gh-pages\"");
    }

    /// <summary>二进制文件使用 base64 创建 blob，不经过文本转码。</summary>
    [Fact]
    public async Task PublishAsync_BinaryFile_CreatesBase64Blob()
    {
        using var output = new TempOutput();
        output.WriteBytes("logo.bin", [0, 1, 2, 3, 255]);
        var handler = new RecordingHandler(
            Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
            Json(HttpStatusCode.Created, """{"sha":"blob-logo"}"""),
            Json(HttpStatusCode.Created, """{"sha":"tree-sha"}"""),
            Json(HttpStatusCode.Created, """{"sha":"commit-sha"}"""),
            Json(HttpStatusCode.Created, """{"ref":"refs/heads/gh-pages","object":{"sha":"commit-sha"}}"""));
        var publisher = CreatePublisher(handler);

        await publisher.PublishAsync(CreateRequest(output.Root, ensurePagesSource: false), default);

        var blobBody = JsonDocument.Parse(handler.Requests[1].Body!);
        blobBody.RootElement.GetProperty("content").GetString().Should().Be("AAECA/8=");
        blobBody.RootElement.GetProperty("encoding").GetString().Should().Be("base64");
    }

    /// <summary>GitHub 错误响应不得把 token 写入异常消息。</summary>
    [Fact]
    public async Task PublishAsync_ApiFailure_DoesNotLeakToken()
    {
        using var output = new TempOutput();
        output.WriteText("index.html", "<h1>Hello</h1>");
        var handler = new RecordingHandler(Json(HttpStatusCode.Forbidden, """{"message":"bad secret-token"}"""));
        var publisher = CreatePublisher(handler);

        var act = async () => await publisher.PublishAsync(CreateRequest(output.Root, ensurePagesSource: false), default);

        await act.Should().ThrowAsync<PublishTargetException>()
            .Where(ex => !ex.Message.Contains("secret-token", StringComparison.Ordinal));
    }

    /// <summary>创建使用记录型 handler 的 publisher。</summary>
    private static GitHubPagesPublisher CreatePublisher(HttpMessageHandler handler)
        => new(new HttpClient(handler));

    /// <summary>创建一份最小发布请求，配置和凭据都走真实 JSON 序列化路径。</summary>
    private static PublishTargetRequest CreateRequest(string publicRoot, bool ensurePagesSource)
    {
        var configuration = new GitHubPagesPublishConfiguration
        {
            Owner = "owner",
            Repository = "site",
            Branch = "gh-pages",
            EnsurePagesSource = ensurePagesSource,
        };
        var credential = new GitHubPagesPublishCredential { Token = "secret-token" };
        return new PublishTargetRequest
        {
            Plan = new PublishPlanRecord
            {
                Id = 1,
                DisplayName = "GitHub Pages",
                Channel = PublishPlanService.GitHubPagesChannel,
                ConfigurationJson = configuration.ToJson(),
            },
            ConfigurationJson = configuration.ToJson(),
            CredentialJson = credential.ToJson(),
            Output = StaticOutputEnumerator.Enumerate(publicRoot),
            BuildResult = new BuildResult
            {
                SessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Mode = BuildMode.FullBuild,
                Status = BuildStatus.Succeeded,
                StartedAt = new DateTimeOffset(2026, 5, 24, 0, 0, 0, TimeSpan.Zero),
                FinishedAt = new DateTimeOffset(2026, 5, 24, 0, 0, 1, TimeSpan.Zero),
                Fingerprint = new BuildFingerprint("1234567890abcdef"),
                Logs = [],
                Artifacts = [],
                BuildRunId = 12,
            },
        };
    }

    /// <summary>创建固定 JSON 响应，供记录型 handler 按顺序返回。</summary>
    private static Func<HttpRequestMessage, string?, HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
        => (_, _) => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>顺序返回预置响应，并记录每个 GitHub API 请求。</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>待返回的响应队列。</summary>
        private readonly Queue<Func<HttpRequestMessage, string?, HttpResponseMessage>> _responses;

        /// <summary>构造记录型 handler。</summary>
        public RecordingHandler(params Func<HttpRequestMessage, string?, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, string?, HttpResponseMessage>>(responses);
        }

        /// <summary>已经发送的请求记录。</summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>记录请求并返回下一个预置响应。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
            return _responses.Dequeue().Invoke(request, body);
        }
    }

    /// <summary>GitHub API 请求记录。</summary>
    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);

    /// <summary>测试用静态输出目录。</summary>
    private sealed class TempOutput : IDisposable
    {
        /// <summary>创建临时输出根目录。</summary>
        public TempOutput()
        {
            Root = Path.Combine(Path.GetTempPath(), "bocchi-github-pages-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        /// <summary>临时输出根目录。</summary>
        public string Root { get; }

        /// <summary>写入文本文件。</summary>
        public void WriteText(string relativePath, string content)
        {
            var path = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        /// <summary>写入二进制文件。</summary>
        public void WriteBytes(string relativePath, byte[] bytes)
        {
            var path = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        /// <summary>删除临时输出目录。</summary>
        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        /// <summary>把 POSIX 相对路径转换为本机路径。</summary>
        private string Resolve(string relativePath)
            => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
