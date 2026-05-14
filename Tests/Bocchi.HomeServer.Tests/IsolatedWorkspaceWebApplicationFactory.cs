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
        if (disposing && Directory.Exists(WorkspaceRoot))
        {
            try
            {
                Directory.Delete(WorkspaceRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup; ignore if some file is locked on Windows.
            }
        }
    }
}
