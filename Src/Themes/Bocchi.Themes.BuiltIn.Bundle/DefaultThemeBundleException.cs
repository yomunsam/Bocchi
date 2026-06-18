namespace Bocchi.Themes.BuiltIn.Bundle;

/// <summary>默认 Theme Bundle 读取或物化失败时抛出的可预期异常。</summary>
public sealed class DefaultThemeBundleException : Exception
{
    /// <summary>创建默认 Theme Bundle 异常。</summary>
    public DefaultThemeBundleException(string message)
        : base(message)
    {
    }
}
