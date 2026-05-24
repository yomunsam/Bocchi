using System.Text.Json;

using Bocchi.HomeServer.Data;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 发布方案服务。它只管理发布目标配置与默认方案，不负责真正执行远端 push；
/// 当前发布执行仍由 BuildOrchestrator 产出本地静态文件。
/// </summary>
public sealed class PublishPlanService
{
    /// <summary>静态文件生成渠道：当前唯一可执行的发布方案。</summary>
    public const string StaticFilesChannel = "static-files";

    /// <summary>本地目录发布渠道：M7 目标，当前仅作为方案类型保留。</summary>
    public const string LocalDirectoryChannel = "local-directory";

    /// <summary>GitHub Pages 发布渠道：M7 目标，当前仅作为方案类型保留。</summary>
    public const string GitHubPagesChannel = "github-pages";

    /// <summary>Cloudflare Pages 发布渠道：M7 目标，当前仅作为方案类型保留。</summary>
    public const string CloudflarePagesChannel = "cloudflare-pages";

    /// <summary>自定义发布命令渠道：后续扩展目标，当前仅作为方案类型保留。</summary>
    public const string CustomChannel = "custom";

    /// <summary>发布渠道 catalog；UI 根据这些稳定 key 展示 icon、名称和支持状态。</summary>
    public static IReadOnlyList<string> ChannelCatalog { get; } =
    [
        StaticFilesChannel,
        LocalDirectoryChannel,
        GitHubPagesChannel,
        CloudflarePagesChannel,
        CustomChannel,
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BocchiDbContext _db;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;

    /// <summary>构造发布方案服务。</summary>
    public PublishPlanService(BocchiDbContext db, IDataProtectionProvider protection, TimeProvider time)
    {
        _db = db;
        _protector = protection.CreateProtector("Bocchi.HomeServer.PublishPlanCredentials.v1");
        _time = time;
    }

    /// <summary>按默认方案优先、更新时间倒序列出全部发布方案。</summary>
    public async Task<IReadOnlyList<PublishPlanRecord>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.PublishPlans
            .AsNoTracking()
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>读取默认发布方案；若旧数据没有默认标记，则回退到最早创建的方案。</summary>
    public async Task<PublishPlanRecord?> GetDefaultAsync(CancellationToken cancellationToken = default)
        => await _db.PublishPlans
               .AsNoTracking()
               .OrderByDescending(x => x.IsDefault)
               .ThenBy(x => x.Id)
               .FirstOrDefaultAsync(cancellationToken)
               .ConfigureAwait(false);

    /// <summary>
    /// 新增或更新发布方案。第一条方案会自动成为默认方案；credentialJson 为 null 时保留已有凭据。
    /// </summary>
    public async Task<PublishPlanRecord> SaveAsync(
        PublishPlanSaveInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var channel = NormalizeChannel(input.Channel);
        var configurationJson = NormalizeJsonObject(input.ConfigurationJson);
        var now = _time.GetUtcNow();
        var hadPlans = await _db.PublishPlans.AnyAsync(cancellationToken).ConfigureAwait(false);
        PublishPlanRecord? record = null;
        if (input.Id is { } id && id > 0)
        {
            record = await _db.PublishPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        }

        if (record is null)
        {
            record = new PublishPlanRecord { CreatedAt = now };
            _db.PublishPlans.Add(record);
        }

        record.DisplayName = NormalizeDisplayName(input.DisplayName, channel);
        record.Channel = channel;
        record.ConfigurationJson = configurationJson;
        record.UpdatedAt = now;

        if (input.CredentialJson is not null)
        {
            record.ProtectedCredentialJson = string.IsNullOrWhiteSpace(input.CredentialJson)
                ? null
                : _protector.Protect(NormalizeJsonObject(input.CredentialJson));
        }

        if (!hadPlans || input.SetAsDefault)
        {
            await ClearDefaultAsync(cancellationToken).ConfigureAwait(false);
            record.IsDefault = true;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <summary>把指定方案设置为一键发布默认方案。</summary>
    public async Task SetDefaultAsync(int id, CancellationToken cancellationToken = default)
    {
        var record = await _db.PublishPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        await ClearDefaultAsync(cancellationToken).ConfigureAwait(false);
        record.IsDefault = true;
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ClearDefaultAsync(CancellationToken cancellationToken)
    {
        var defaults = await _db.PublishPlans
            .Where(x => x.IsDefault)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in defaults)
        {
            item.IsDefault = false;
        }
    }

    private static string NormalizeDisplayName(string? displayName, string channel)
    {
        var trimmed = displayName?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return channel switch
        {
            StaticFilesChannel => "Static files",
            LocalDirectoryChannel => "Local directory",
            GitHubPagesChannel => "GitHub Pages",
            CloudflarePagesChannel => "Cloudflare Pages",
            CustomChannel => "Custom publish",
            _ => channel,
        };
    }

    private static string NormalizeChannel(string? channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel)
            ? StaticFilesChannel
            : channel.Trim().ToLowerInvariant();
        if (!ChannelCatalog.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported publish channel: {channel}", nameof(channel));
        }

        return normalized;
    }

    private static string NormalizeJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Publish plan JSON must be an object.", nameof(json));
        }

        return JsonSerializer.Serialize(document.RootElement, JsonOptions);
    }
}

/// <summary>保存发布方案所需的输入模型；credentialJson 为 null 表示不修改已有凭据。</summary>
public sealed record PublishPlanSaveInput(
    int? Id,
    string DisplayName,
    string Channel,
    string? ConfigurationJson,
    string? CredentialJson,
    bool SetAsDefault);
