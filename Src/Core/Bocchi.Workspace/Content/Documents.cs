using Bocchi.ContentModel;

namespace Bocchi.Workspace.Content;

/// <summary>已解析的文章。</summary>
public sealed record PostDocument(ContentLocation Location, string Year, Post Frontmatter, ContentBody Body);

/// <summary>已解析的独立页面。</summary>
public sealed record PageDocument(ContentLocation Location, Page Frontmatter, ContentBody Body);

/// <summary>已解析的作品。</summary>
public sealed record WorkDocument(ContentLocation Location, string Year, Work Frontmatter, ContentBody Body);

/// <summary>
/// 已解析的短文。短文的"正文"即 Markdown 正文，因此 <see cref="Note.Text"/> 等于
/// <see cref="ContentBody.Markdown"/>，避免双重事实来源。
/// </summary>
public sealed record NoteDocument(ContentLocation Location, string Year, Note Frontmatter, ContentBody Body);
