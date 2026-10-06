using Bocchi.HomeServer.Hosting;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Tests;

public sealed class DataRootResolverTests
{
    private static IConfiguration Config(string? dataRoot)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DataRootResolver.ConfigurationKey] = dataRoot })
            .Build();

    private static HostingEnvironment Env(string name)
        => new() { EnvironmentName = name, ContentRootPath = Path.Combine(Path.GetTempPath(), "bocchi-content-root") };

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Production_WithoutDataRoot_FailsWithHint(string? configured)
    {
        var act = () => DataRootResolver.Resolve(Config(configured), Env(Environments.Production));

        act.Should().Throw<BocchiStartupException>().WithMessage("*Bocchi__DataRoot*");
    }

    [Fact]
    public void Production_WithRelativeDataRoot_Fails()
    {
        var act = () => DataRootResolver.Resolve(Config("data"), Env(Environments.Production));

        act.Should().Throw<BocchiStartupException>().WithMessage("*绝对路径*");
    }

    [Fact]
    public void Production_WithAbsoluteDataRoot_ReturnsIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-prod-data");

        DataRootResolver.Resolve(Config(root), Env(Environments.Production)).Should().Be(root);
    }

    [Fact]
    public void Development_WithoutDataRoot_UsesDevDirectoryUnderContentRoot()
    {
        var env = Env(Environments.Development);

        DataRootResolver.Resolve(Config(null), env)
            .Should().Be(Path.Combine(env.ContentRootPath, DataRootResolver.DevelopmentDefault));
    }

    [Fact]
    public async Task DataProtection_PersistsKeysUnderDataRootWithFixedApplicationName()
    {
        await using var factory = new IsolatedDataRootWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        protector.Unprotect(protector.Protect("secret")).Should().Be("secret");

        Directory.EnumerateFiles(Path.Combine(factory.DataRoot, "keys"), "key-*.xml").Should().NotBeEmpty();
        scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator
            .Should().Be(ServerInfo.DataProtectionApplicationName);
    }
}
