namespace Bocchi.Generator.Exceptions;

/// <summary>
/// Generator 模块所有失败模式的基类。所有 M3 阶段抛出的非系统异常都派生自此类型，
/// 便于上层统一捕获并归档到构建日志。
/// </summary>
public abstract class GeneratorException : Exception
{
    protected GeneratorException(string message)
        : base(message)
    {
    }

    protected GeneratorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
