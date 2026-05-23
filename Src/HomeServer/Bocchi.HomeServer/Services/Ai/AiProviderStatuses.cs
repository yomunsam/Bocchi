namespace Bocchi.HomeServer.Services.Ai;

/// <summary>Dashboard AI provider 的通用状态值。</summary>
public static class AiProviderStatuses
{
    /// <summary>provider 已经可以直接调用。</summary>
    public const string Available = "available";

    /// <summary>provider 可下载但尚未准备好。</summary>
    public const string Downloadable = "downloadable";

    /// <summary>provider 正在由浏览器下载或准备。</summary>
    public const string Downloading = "downloading";

    /// <summary>provider 在当前浏览器或设备不可用。</summary>
    public const string Unavailable = "unavailable";

    /// <summary>provider 探测过程中发生错误。</summary>
    public const string Error = "error";
}
