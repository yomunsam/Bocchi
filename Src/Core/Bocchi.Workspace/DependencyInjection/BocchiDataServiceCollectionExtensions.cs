using Bocchi.Workspace.Content;
using Bocchi.Workspace.Content.Loaders;
using Bocchi.Workspace.Git;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

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
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="configuration">用于绑定 <see cref="BocchiDataOptions"/> 的根配置。</param>
    /// <param name="dataRootBaseResolver">
    /// 在 <see cref="BocchiDataOptions.DataRoot"/> 为空或为相对路径时使用的基准路径提供者。
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
                var fallback = dataRootBaseResolver(sp);
                root = Path.Combine(fallback, "data");
            }
            else if (!Path.IsPathRooted(root))
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

        services.TryAddSingleton<SqliteConnectionFactory>();
        services.TryAddSingleton<SchemaMigrator>();
        services.TryAddSingleton<IContentStateStore, ContentStateStore>();

        services.TryAddSingleton<IContentRepository, LibGit2ContentRepository>();

        services.TryAddSingleton<ContentScanner>();

        return services;
    }
}
