namespace Bocchi.Generator.Pipeline;

/// <summary>构建产物指纹。详见 <c>Docs/Milestones/M3/M3.md §3.7</c>。</summary>
/// <param name="Value">小写十六进制 SHA-256 字符串。</param>
public readonly record struct BuildFingerprint(string Value)
{
    public override string ToString() => Value;
}
