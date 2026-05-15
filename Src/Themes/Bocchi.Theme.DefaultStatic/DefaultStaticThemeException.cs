namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme renderer 的可预期错误。</summary>
public sealed class DefaultStaticThemeException : Exception
{
    /// <summary>创建只有错误消息的 Theme 渲染异常。</summary>
    public DefaultStaticThemeException(string message)
        : base(message)
    {
    }

    /// <summary>创建包装底层异常的 Theme 渲染异常。</summary>
    public DefaultStaticThemeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
