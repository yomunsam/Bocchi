using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Bocchi.HomeServer.Tests;

/// <summary>
/// Custom factory that points the workspace at a per-instance temp directory,
/// so integration tests don't pollute the source tree or share state.
/// </summary>
public sealed class IsolatedWorkspaceWebApplicationFactory
    : WebApplicationFactory<Program>
{
    public string WorkspaceRoot { get; } =
        Path.Combine(Path.GetTempPath(), "bocchi-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bocchi:WorkspaceRoot"] = WorkspaceRoot,
            });
        });
        base.ConfigureWebHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            DeleteBestEffort(WorkspaceRoot);
        }
    }

    public string SeedPublishedPostWithMedia()
    {
        var postDir = Path.Combine(WorkspaceRoot, "content", "posts", "2026", "hello-preview");
        Directory.CreateDirectory(Path.Combine(postDir, "assets"));
        File.WriteAllText(Path.Combine(postDir, "index.md"),
            "---\ntitle: Hello Preview\nslug: hello-preview\nstatus: Published\npublishedAt: 2026-05-14T12:00:00Z\n---\nPreview body ![cover](assets/cover.jpg)\n");
        var mediaPath = Path.Combine(postDir, "assets", "cover.jpg");
        File.WriteAllBytes(mediaPath, [0xFF, 0xD8, 0xFF, 0xE0]);
        return "/media/posts/2026/hello-preview/cover.jpg";
    }

    private static void DeleteBestEffort(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
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