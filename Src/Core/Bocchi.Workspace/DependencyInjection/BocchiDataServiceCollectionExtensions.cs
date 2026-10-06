using Bocchi.Workspace.Content;
using Bocchi.Workspace.Content.Loaders;
using Bocchi.Workspace.Git;
using Bocchi.Workspace.Scanning;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bocchi.Workspace.DependencyInjection;

/// <summary>
/// 把 Bocchi 数据根和内容 workspace 相关服务注册到 DI 容器的扩展。
/// </summary>
public static class BocchiDataServiceCollectionExtensions
{
    /// <summary>
    /// 注册 DataRoot 与内容 workspace 相关服务。
    /// <see cref="State.IContentStateStore"/> 的持久化实现由宿主注册。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="configuration">用于绑定 <see cref="BocchiDataOptions"/> 的根配置。</param>
    /// <param name="dataRootBaseResolver">
    /// <see cref="BocchiDataOptions.DataRoot"/> 为相对路径时使用的基准路径提供者。
    /// </param>
    public static IServiceCollection AddBocchiData(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, string> dataRootBaseResolver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(dataRootBaseResolver);

        services.AddOptions<BocchiDataOptions>()
            .Bind(configuration.GetSection(BocchiDataOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BocchiDataOptions>>().Value;
            var root = opts.DataRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                // 不再回退到程序目录：DataRoot 由宿主显式提供。
                throw new InvalidOperationException("Bocchi:DataRoot 未配置。");
            }

            if (!Path.IsPathRooted(root))
            {
                var basePath = dataRootBaseResolver(sp);
                root = Path.GetFullPath(Path.Combine(basePath, root));
            }

            return new BocchiDataLayout(root);
        });

        services.TryAddSingleton(sp => sp.GetRequiredService<BocchiDataLayout>().Workspace);

        services.TryAddSingleton<BocchiDataInitializer>();
        services.TryAddSingleton<IWorkspace>(sp => new Workspace(sp.GetRequiredService<WorkspaceLayout>()));
        services.TryAddSingleton<IWorkspaceLoader, WorkspaceLoader>();

        services.TryAddSingleton<MarkdownPipeline>();
        services.TryAddSingleton<PostLoader>();
        services.TryAddSingleton<PageLoader>();
        services.TryAddSingleton<WorkLoader>();
        services.TryAddSingleton<NoteLoader>();

        services.TryAddSingleton<IContentRepository, LibGit2ContentRepository>();

        services.TryAddSingleton<ContentScanner>();

        return services;
    }
}
