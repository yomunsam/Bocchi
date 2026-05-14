namespace Bocchi.Generator.Pipeline;

/// <summary>构建阶段。所有 Stage 都通过 DI 注入；执行顺序由 <see cref="GeneratorPipeline"/> 固定。</summary>
public interface IBuildStage
{
    /// <summary>稳定的英文阶段名，用于日志与持久化。</summary>
    string Name { get; }

    /// <summary>执行阶段。</summary>
    /// <returns><c>true</c> 表示继续后续阶段；<c>false</c> 表示当前阶段决定整次构建短路（例如 up-to-date）。</returns>
    Task<bool> ExecuteAsync(BuildSession session);
}