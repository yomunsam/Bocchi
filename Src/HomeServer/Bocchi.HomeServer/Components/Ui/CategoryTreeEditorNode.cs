namespace Bocchi.HomeServer.Components.Ui;

/// <summary>
/// Category 树编辑器的可变节点模型。它只用于 Blazor 交互缓冲，保存时再映射为服务层不可变快照。
/// </summary>
public sealed class CategoryTreeEditorNode
{
    /// <summary>稳定节点 id；新建节点立即分配，避免编辑期间列表 diff 抖动。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>类别显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>类别 URL slug；为空保存时会由服务层从名称自动生成。</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>下一层子类别。</summary>
    public List<CategoryTreeEditorNode> Children { get; } = [];
}
