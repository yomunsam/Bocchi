namespace Bocchi.HomeServer.Hosting;

/// <summary>可预期的启动配置错误。入口只输出消息，不打印堆栈，方便用户直接照提示修正。</summary>
public sealed class BocchiStartupException : Exception
{
    /// <summary>创建启动配置错误。</summary>
    public BocchiStartupException(string message)
        : base(message)
    {
    }
}
