namespace Bocchi.Generator.Theme;

/// <summary>
/// Theme 外部进程相关错误：可执行未找到、退出码非零、超时、stdout/stderr 解析失败等。
/// </summary>
public sealed class ThemeRunnerException : Bocchi.Generator.Exceptions.GeneratorException
{
    public ThemeRunnerException(string message)
        : base(message)
    {
    }

    public ThemeRunnerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}