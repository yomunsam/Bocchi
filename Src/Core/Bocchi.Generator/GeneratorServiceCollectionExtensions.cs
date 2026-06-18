using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Pipeline.Stages;
using Bocchi.Generator.State;
using Bocchi.Generator.Theme;
using Bocchi.Generator.ThemeInputs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bocchi.Generator;

/// <summary>Generator 模块的 DI 注册扩展。</summary>
public static class GeneratorServiceCollectionExtensions
{
    /// <summary>注册 Generator 流水线的全部组件。前置依赖：Workspace 服务已注册。</summary>
    public static IServiceCollection AddBocchiGenerator(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<GeneratorOptions>(configuration.GetSection(GeneratorOptions.SectionName));
        services.Configure<ThemeDevelopmentOptions>(configuration.GetSection(ThemeDevelopmentOptions.SectionName));
        services.Configure<ThemePackageOptions>(configuration.GetSection(ThemePackageOptions.SectionName));

        services.AddSingleton<ContentGraphBuilder>();
        services.AddSingleton<ThemeInputWriter>();
        services.AddSingleton<IThemeRunner, ThemeRunner>();
        services.TryAddSingleton<ThemeResolver>();
        services.TryAddSingleton<ThemePackageService>();
        services.AddSingleton<IBuildStateStore, BuildStateStore>();

        services.AddSingleton<LoadContentStage>();
        services.AddSingleton<BuildContentGraphStage>();
        services.AddSingleton<ComputeFingerprintStage>();
        services.AddSingleton<ShortCircuitIfUpToDateStage>();
        services.AddSingleton<LoadThemeStage>();
        services.AddSingleton<WriteThemeInputStage>();
        services.AddSingleton<WriteSiteArtifactsStage>();
        services.AddSingleton<CopyMediaStage>();
        services.AddSingleton<RunThemeBuildStage>();
        services.AddSingleton<CopyThemeStaticAssetsStage>();
        services.AddSingleton<CollectThemeOutputStage>();
        services.AddSingleton<ValidateOutputStage>();
        services.AddSingleton<WriteManifestStage>();

        services.AddTransient<GeneratorPipeline>();
        return services;
    }
}
