namespace Bocchi.Generator.Pipeline;

/// <summary>
/// 构建模式。<see cref="FullBuild"/> 写到文件系统，<see cref="Live"/> 流式吐到 HTTP，<see cref="DryRun"/> 仅断言。
/// </summary>
public enum BuildMode
{
    /// <summary>完整构建到本地文件系统。</summary>
    FullBuild,

    /// <summary>实时模式：单 artifact 流式输出（HomeServer 预览端点）。</summary>
    Live,

    /// <summary>仅断言不写。</summary>
    DryRun,
}
