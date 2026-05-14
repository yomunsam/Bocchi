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
/// 把 Bocchi.Workspace 注册到 DI 容器的扩展。
/// </summary>
public static class WorkspaceServiceCollectionExtensions
{
    /// <summary>
    /// 注册工作区相关服务。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="configuration">用于绑定 <see cref="WorkspaceOptions"/> 的根配置。</param>
    /// <param name="workspaceRootResolver">
    /// 在 <see cref="WorkspaceOptions.WorkspaceRoot"/> 为空时回退使用的根路径提供者；
    /// 通常 Web 宿主传入 <c>builder.Environment.ContentRootPath</c>。
    /// </param>
    public static IServiceCollection AddBocchiWorkspace(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, string> workspaceRootResolver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(workspaceRootResolver);

        services.AddOptions<WorkspaceOptions>()
            .Bind(configuration.GetSection(WorkspaceOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkspaceOptions>>().Value;
            var root = opts.WorkspaceRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                var fallback = workspaceRootResolver(sp);
                root = Path.Combine(fallback, "workspace");
            }
            else if (!Path.IsPathRooted(root))
            {
                var basePath = workspaceRootResolver(sp);
                root = Path.GetFullPath(Path.Combine(basePath, root));
            }

            return new WorkspaceLayout(root);
        });

        services.TryAddSingleton(sp => sp.GetRequiredService<WorkspaceLayout>().ContentSpace);

        services.TryAddSingleton<WorkspaceInitializer>();
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
