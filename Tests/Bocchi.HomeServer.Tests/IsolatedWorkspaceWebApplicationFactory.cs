using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Bocchi.HomeServer.Data;

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

    public const string AdminEmail = "admin@example.test";

    public const string AdminPassword = "soft-password";

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

    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var setup = await client.GetAsync("/Setup");
        var setupBody = await setup.Content.ReadAsStringAsync();
        if (setup.IsSuccessStatusCode && setupBody.Contains("Create Admin", StringComparison.Ordinal))
        {
            var created = await client.PostAsync("/Setup", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = AdminEmail,
                ["displayName"] = "Bocchi Admin",
                ["password"] = AdminPassword,
                ["confirmPassword"] = AdminPassword,
            }));
            created.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
            created.Headers.Location!.ToString().Should().Be("/Admin");
            return client;
        }

        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = AdminEmail,
            ["password"] = AdminPassword,
            ["remember"] = "on",
        }));
        login.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        return client;
    }

    public async Task CreateLocalUserAsync(string email, string password, bool isAdmin)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BocchiUser>>();
        var user = new BocchiUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = email,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var create = await users.CreateAsync(user, password);
        create.Succeeded.Should().BeTrue(string.Join("; ", create.Errors.Select(x => x.Description)));
        if (isAdmin)
        {
            var role = await users.AddToRoleAsync(user, BocchiRoleNames.Admin);
            role.Succeeded.Should().BeTrue(string.Join("; ", role.Errors.Select(x => x.Description)));
        }
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
