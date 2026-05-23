namespace Bocchi.HomeServer.Components.Ui;

/// <summary>
/// <see cref="BocchiLocalizedInput"/> 的输入形态：单行 input 或多行 textarea。
/// </summary>
public enum BocchiLocalizedInputMode
{
    /// <summary>普通单行输入框。</summary>
    SingleLine,

    /// <summary>多行 textarea，适合 LocalizedTextList 或长描述。</summary>
    MultiLine,
}
