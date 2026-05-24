namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>发布目标 publisher 接口；每个实现只处理一个渠道的远端语义。</summary>
public interface IPublishTargetPublisher
{
    /// <summary>当前 publisher 支持的发布渠道 key。</summary>
    string Channel { get; }

    /// <summary>把本地静态输出发布到远端目标。</summary>
    Task<PublishTargetResult> PublishAsync(PublishTargetRequest request, CancellationToken cancellationToken);
}
