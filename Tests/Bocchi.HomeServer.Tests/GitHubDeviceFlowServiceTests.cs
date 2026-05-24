using System.Net;
using System.Text;

using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Git;
using Bocchi.HomeServer.Services.Publishing;

using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Tests;

/// <summary>验证 GitHub Device Flow 的轮询状态映射和 token 响应解析。</summary>
public sealed class GitHubDeviceFlowServiceTests
{
    /// <summary>GitHub Device Flow 的等待、降速、拒绝、过期状态会映射为明确的内部状态。</summary>
    [Theory]
    [InlineData("authorization_pending", GitHubDeviceFlowPollStatus.Pending, false)]
    [InlineData("slow_down", GitHubDeviceFlowPollStatus.SlowDown, true)]
    [InlineData("access_denied", GitHubDeviceFlowPollStatus.AccessDenied, false)]
    [InlineData("expired_token", GitHubDeviceFlowPollStatus.Expired, false)]
    public async Task PollAsync_GitHubError_ReturnsExpectedStatus(string error, GitHubDeviceFlowPollStatus expectedStatus, bool increasePollingInterval)
    {
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, $$"""{"error":"{{error}}","error_description":"description"}"""));
        await using var context = await CreateServiceAsync(handler);

        var result = await context.Service.PollAsync("device-code");

        result.Status.Should().Be(expectedStatus);
        result.Credential.Should().BeNull();
        result.ErrorDescription.Should().Be("description");
        result.IncreasePollingInterval.Should().Be(increasePollingInterval);
    }

    /// <summary>授权成功后返回 OAuth credential，但不会把 token 写入 URL 或额外状态。</summary>
    [Fact]
    public async Task PollAsync_Success_ReturnsCredential()
    {
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, """{"access_token":"secret-token","token_type":"bearer","scope":"repo read:user"}"""));
        await using var context = await CreateServiceAsync(handler);

        var result = await context.Service.PollAsync("device-code");

        result.Status.Should().Be(GitHubDeviceFlowPollStatus.Succeeded);
        result.Credential.Should().NotBeNull();
        result.Credential!.AccessToken.Should().Be("secret-token");
        result.Credential.Scope.Should().Be("repo read:user");
        handler.Requests.Single().Uri.ToString().Should().NotContain("secret-token");
    }

    /// <summary>启动 Device Flow 时请求 repo 和基础用户信息权限。</summary>
    [Fact]
    public async Task StartAsync_RequestsRepoAndUserScopes()
    {
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, """{"device_code":"device-code","user_code":"ABCD-EFGH","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));
        await using var context = await CreateServiceAsync(handler);

        var result = await context.Service.StartAsync();

        result.UserCode.Should().Be("ABCD-EFGH");
        handler.Requests.Single().Body.Should().Contain("scope=repo+read%3Auser");
    }

    /// <summary>GitHub Integration 保存的 Client ID 优先于 appsettings，便于 NAS 用户在 UI 中完成配置。</summary>
    [Fact]
    public async Task StartAsync_UsesIntegrationSavedClientId()
    {
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, """{"device_code":"device-code","user_code":"ABCD-EFGH","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));
        await using var context = await CreateServiceAsync(handler, savedClientId: "saved-client-id");

        await context.Service.StartAsync();

        handler.Requests.Single().Body.Should().Contain("client_id=saved-client-id");
    }

    /// <summary>GitHub API 错误消息即使回显 token，也不会把明文 token 交给 UI 或日志。</summary>
    [Fact]
    public async Task GetCurrentUserAsync_ApiFailure_DoesNotLeakToken()
    {
        var handler = new RecordingHandler(
            Json(HttpStatusCode.Forbidden, """{"message":"bad secret-token"}"""));
        await using var context = await CreateServiceAsync(handler);

        var act = async () => await context.Service.GetCurrentUserAsync("secret-token");

        await act.Should().ThrowAsync<PublishTargetException>()
            .Where(ex => !ex.Message.Contains("secret-token", StringComparison.Ordinal));
    }

    /// <summary>创建测试用 GitHub Device Flow 服务。</summary>
    private static async Task<ServiceContext> CreateServiceAsync(HttpMessageHandler handler, string? savedClientId = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new BocchiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "bocchi-github-device-flow-tests", Guid.NewGuid().ToString("N"))));
        var settings = new GitHubIntegrationSettingsService(
            db,
            protection,
            TimeProvider.System,
            new OptionsCache<OAuthOptions>());
        if (!string.IsNullOrWhiteSpace(savedClientId))
        {
            await settings.SaveAsync(new GitHubIntegrationSettingsUpdate(false, "GitHub", savedClientId, null, "/signin-github"));
        }

        var service = new GitHubDeviceFlowService(
            new HttpClient(handler),
            Options.Create(new GitHubDeviceFlowOptions { OAuthClientId = "client-id" }),
            settings);
        return new ServiceContext(connection, db, service);
    }

    /// <summary>创建固定 JSON 响应。</summary>
    private static Func<HttpRequestMessage, string?, HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
        => (_, _) => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>记录请求并按顺序返回响应。</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>待返回响应。</summary>
        private readonly Queue<Func<HttpRequestMessage, string?, HttpResponseMessage>> _responses;

        /// <summary>构造记录型 handler。</summary>
        public RecordingHandler(params Func<HttpRequestMessage, string?, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, string?, HttpResponseMessage>>(responses);
        }

        /// <summary>已发送请求。</summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>记录请求并返回下一个响应。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
            return _responses.Dequeue().Invoke(request, body);
        }
    }

    /// <summary>HTTP 请求记录。</summary>
    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);

    /// <summary>测试用服务与内存数据库生命周期。</summary>
    private sealed class ServiceContext : IAsyncDisposable
    {
        /// <summary>构造测试上下文。</summary>
        public ServiceContext(SqliteConnection connection, BocchiDbContext db, GitHubDeviceFlowService service)
        {
            Connection = connection;
            Db = db;
            Service = service;
        }

        /// <summary>SQLite 内存连接。</summary>
        private SqliteConnection Connection { get; }

        /// <summary>测试数据库。</summary>
        private BocchiDbContext Db { get; }

        /// <summary>待测服务。</summary>
        public GitHubDeviceFlowService Service { get; }

        /// <summary>释放数据库资源。</summary>
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
