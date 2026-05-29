using Bocchi.Generator;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.Workspace;
using Bocchi.Workspace.Content;
using Bocchi.Workspace.Content.Loaders;
using Bocchi.Workspace.DependencyInjection;
using Bocchi.Workspace.Git;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bocchi.Generator.Tests;

/// <summary>构造一个真实的 workspace + 已扫描内容 + 完整 DI 容器，用于端到端 pipeline 测试。</summary>
internal sealed class TestWorkspaceFixture : IDisposable
{
    public string Root { get; }

    public BocchiDataLayout Layout { get; }

    public ServiceProvider Services { get; }

    public TestWorkspaceFixture(Action<IServiceCollection>? configureServices = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "bocchi-gen-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Layout = new BocchiDataLayout(Root);

        var initTask = new BocchiDataInitializer(Layout).InitializeAsync();
        initTask.GetAwaiter().GetResult();

        // 写一些示例内容
        SeedContent();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bocchi:DataRoot"] = Root,
                ["Bocchi:AutoInitialize"] = "false",
                ["Bocchi:AutoMigrateSchema"] = "false",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddBocchiData(configuration, _ => Root);
        services.AddBocchiGenerator(configuration);
        configureServices?.Invoke(services);

        Services = services.BuildServiceProvider();

        // 显式迁移 schema（关闭 AutoMigrateSchema 时也确保可用）
        var migrator = Services.GetRequiredService<SchemaMigrator>();
        migrator.MigrateAsync().GetAwaiter().GetResult();
    }

    private void SeedContent()
    {
        var cs = Layout.Workspace;
        var postDir = Path.Combine(cs.PostsDirectory, "2025", "hello");
        Directory.CreateDirectory(Path.Combine(postDir, "assets"));
        File.WriteAllText(Path.Combine(postDir, "index.md"),
            "---\ntitle: Hello\nslug: hello\nstatus: Published\npublishedAt: 2025-03-14T08:00:00Z\ntags: [a, b]\n---\nBody ![alt](assets/c.jpg)\n");
        File.WriteAllBytes(Path.Combine(postDir, "assets", "c.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        var pageDir = Path.Combine(cs.PagesDirectory, "about");
        Directory.CreateDirectory(pageDir);
        File.WriteAllText(Path.Combine(pageDir, "index.md"),
            "---\ntitle: About\nslug: about\nstatus: Published\n---\nAbout body.\n");

        var noteDir = Path.Combine(cs.NotesDirectory, "2025", "0314", "1230-k7p9xq2m");
        Directory.CreateDirectory(Path.Combine(noteDir, "assets"));
        File.WriteAllText(Path.Combine(noteDir, "index.md"),
            "---\nid: k7p9xq2m\nstatus: Published\npublishedAt: 2025-03-14T09:00:00Z\nmedia:\n  - assets/note.jpg\n---\nNote body ![note](assets/note.jpg)\n");
        File.WriteAllBytes(Path.Combine(noteDir, "assets", "note.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
    }

    public void Dispose()
    {
        Services.Dispose();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }
}
