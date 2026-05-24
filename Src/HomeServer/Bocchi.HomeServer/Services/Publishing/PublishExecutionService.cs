using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>发布执行服务：生成静态输出、校验本地文件，再分发给具体远端 publisher。</summary>
public sealed class PublishExecutionService
{
    /// <summary>Home Server 状态数据库，用于读取方案并写入发布运行。</summary>
    private readonly BocchiDbContext _db;

    /// <summary>发布方案服务，负责配置查询和凭据保护/解保护。</summary>
    private readonly PublishPlanService _plans;

    /// <summary>静态站点构建入口。</summary>
    private readonly IStaticSiteBuildRunner _buildRunner;

    /// <summary>DataRoot 布局，用于定位 <c>output/public/</c>。</summary>
    private readonly BocchiDataLayout _layout;

    /// <summary>当前进程已注册的发布目标实现。</summary>
    private readonly IReadOnlyList<IPublishTargetPublisher> _publishers;

    /// <summary>时间来源，测试可替换。</summary>
    private readonly TimeProvider _time;

    /// <summary>构造发布执行服务。</summary>
    public PublishExecutionService(
        BocchiDbContext db,
        PublishPlanService plans,
        IStaticSiteBuildRunner buildRunner,
        BocchiDataLayout layout,
        IEnumerable<IPublishTargetPublisher> publishers,
        TimeProvider time)
    {
        _db = db;
        _plans = plans;
        _buildRunner = buildRunner;
        _layout = layout;
        _publishers = publishers.ToArray();
        _time = time;
    }

    /// <summary>按 id 执行发布方案。</summary>
    public async Task<PublishExecutionResult> PublishAsync(int planId, CancellationToken cancellationToken = default)
    {
        var plan = await _plans.GetAsync(planId, cancellationToken).ConfigureAwait(false);
        return plan is null
            ? CreateUnpersistedFailure(PublishPlanService.GitHubPagesChannel, "GitHub Pages", "发布方案不存在。")
            : await PublishPlanAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>执行最近的 GitHub Pages 发布方案；HTTP endpoint 使用它保持请求体最小。</summary>
    public async Task<PublishExecutionResult> PublishLatestGitHubPagesAsync(CancellationToken cancellationToken = default)
    {
        var plan = await _plans
            .GetLatestByChannelAsync(PublishPlanService.GitHubPagesChannel, cancellationToken)
            .ConfigureAwait(false);
        return plan is null
            ? CreateUnpersistedFailure(PublishPlanService.GitHubPagesChannel, "GitHub Pages", "请先配置 GitHub Pages 发布方案。")
            : await PublishPlanAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取最近发布运行历史。</summary>
    public async Task<IReadOnlyList<PublishRunRecord>> ListRecentRunsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var capped = limit <= 0 ? 20 : Math.Min(limit, 100);
        return await _db.PublishRuns
            .AsNoTracking()
            // SQLite 不能直接按 DateTimeOffset 排序；发布运行按创建顺序写入，Id 足以表达“最近”。
            .OrderByDescending(x => x.Id)
            .Take(capped)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>执行发布方案的完整生命周期，并把脱敏结果持久化。</summary>
    private async Task<PublishExecutionResult> PublishPlanAsync(PublishPlanRecord plan, CancellationToken cancellationToken)
    {
        var run = new PublishRunRecord
        {
            PublishPlanId = plan.Id,
            DisplayName = plan.DisplayName,
            Channel = plan.Channel,
            StartedAt = _time.GetUtcNow(),
            Status = PublishRunStatus.Running,
        };
        _db.PublishRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        BuildResult? build = null;
        string? credentialJson = null;
        try
        {
            build = await _buildRunner.RunPublishBuildAsync(cancellationToken).ConfigureAwait(false);
            ApplyBuildSnapshot(run, build);
            if (build.Status == BuildStatus.Failed)
            {
                throw new PublishTargetException(build.Reason ?? "静态站点构建失败。");
            }

            var output = StaticOutputEnumerator.Enumerate(_layout.PublicOutputDirectory);
            run.ArtifactCount = output.Files.Count;

            var publisher = ResolvePublisher(plan.Channel);
            credentialJson = _plans.UnprotectCredentialJson(plan);
            var targetResult = await publisher.PublishAsync(
                new PublishTargetRequest
                {
                    Plan = plan,
                    ConfigurationJson = plan.ConfigurationJson,
                    CredentialJson = credentialJson,
                    Output = output,
                    BuildResult = build,
                },
                cancellationToken).ConfigureAwait(false);

            run.Status = PublishRunStatus.Succeeded;
            run.RemoteCommitSha = targetResult.RemoteCommitSha;
            run.RemoteUrl = targetResult.RemoteUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyFailure(run, ex, credentialJson, plan.ProtectedCredentialJson);
        }
        finally
        {
            run.FinishedAt = _time.GetUtcNow();
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return ToResult(run, build?.Status);
    }

    /// <summary>根据渠道选择具体 publisher。</summary>
    private IPublishTargetPublisher ResolvePublisher(string channel)
    {
        var publisher = _publishers.SingleOrDefault(x => string.Equals(x.Channel, channel, StringComparison.Ordinal));
        return publisher ?? throw new PublishTargetException($"发布渠道 '{channel}' 尚未接入执行器。");
    }

    /// <summary>把构建摘要写入发布运行记录。</summary>
    private static void ApplyBuildSnapshot(PublishRunRecord run, BuildResult build)
    {
        run.BuildRunId = build.BuildRunId;
        run.BuildSessionId = build.SessionId.ToString("D");
        run.BuildFingerprint = build.Fingerprint?.Value;
    }

    /// <summary>把失败信息写入发布运行记录，并保留远端已完成的 commit 信息。</summary>
    private static void ApplyFailure(PublishRunRecord run, Exception exception, params string?[] secrets)
    {
        run.Status = PublishRunStatus.Failed;
        if (exception is PublishTargetException publishException)
        {
            run.RemoteCommitSha = publishException.RemoteCommitSha;
            run.RemoteUrl = publishException.RemoteUrl;
        }

        run.ErrorMessage = PublishSecretSanitizer.Sanitize(exception.Message, secrets);
    }

    /// <summary>把持久化记录转换为 API/UI 结果。</summary>
    private static PublishExecutionResult ToResult(PublishRunRecord run, BuildStatus? buildStatus)
        => new()
        {
            PublishRunId = run.Id,
            Status = run.Status,
            Channel = run.Channel,
            DisplayName = run.DisplayName,
            BuildStatus = buildStatus,
            BuildSessionId = string.IsNullOrWhiteSpace(run.BuildSessionId) ? null : Guid.Parse(run.BuildSessionId),
            BuildFingerprint = run.BuildFingerprint,
            ArtifactCount = run.ArtifactCount,
            RemoteCommitSha = run.RemoteCommitSha,
            RemoteUrl = run.RemoteUrl,
            ErrorMessage = run.ErrorMessage,
        };

    /// <summary>创建没有持久化运行记录的失败结果，常用于配置缺失。</summary>
    private static PublishExecutionResult CreateUnpersistedFailure(string channel, string displayName, string errorMessage)
        => new()
        {
            Status = PublishRunStatus.Failed,
            Channel = channel,
            DisplayName = displayName,
            ErrorMessage = errorMessage,
        };
}
