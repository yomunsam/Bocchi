using Bocchi.Workspace.State;

namespace Bocchi.HomeServer.Components.Ui;

public partial class BocchiContentKindIndex
{
    /// <summary>内容列表展示组；多语言内容组以代表 variant 作为主行，其余 variant 放入展开层。</summary>
    private sealed record ContentSummaryGroup(
        string Key,
        ContentSummary Representative,
        ContentSummary[] Variants)
    {
        /// <summary>是否存在多个语言版本，用于决定是否显示展开入口。</summary>
        public bool HasMultipleVariants => Variants.Length > 1;

        /// <summary>主行展示路径；多语言组代表内容文件夹，单语言内容继续代表具体文件。</summary>
        public string DisplayRelativePath => HasMultipleVariants ? ContainingFolderPath(Representative.RelativePath) : Representative.RelativePath;

        /// <summary>从 Markdown 文件路径取内容文件夹路径，避免多语言组主行误表现为某个具体 variant。</summary>
        private static string ContainingFolderPath(string relativePath)
        {
            var normalizedPath = relativePath.Replace('\\', '/');
            var slashIndex = normalizedPath.LastIndexOf('/');
            return slashIndex > 0 ? normalizedPath[..slashIndex] : normalizedPath;
        }
    }
}
