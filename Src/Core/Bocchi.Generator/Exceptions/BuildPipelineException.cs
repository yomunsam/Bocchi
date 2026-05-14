namespace Bocchi.Generator.Exceptions;

/// <summary>
/// 流水线 / Sink 层的运行时错误：写文件失败、路径穿越、manifest 一致性等。
/// </summary>
public sealed class BuildPipelineException : GeneratorException
{
    public BuildPipelineException(string message)
        : base(message)
    {
    }

    public BuildPipelineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
