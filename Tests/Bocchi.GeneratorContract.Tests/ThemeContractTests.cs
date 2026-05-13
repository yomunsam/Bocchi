namespace Bocchi.GeneratorContract.Tests;

public sealed class ThemeContractTests
{
    [Fact]
    public void ContractVersion_IsV1()
    {
        ThemeContractVersion.Current.Should().Be(ThemeContractVersion.V1);
        ThemeContractVersion.V1.Should().Be("1.0");
    }

    [Fact]
    public void ThemeManifest_CanBeConstructedWithRequiredFields()
    {
        var manifest = new ThemeManifest
        {
            Id = "default-svelte",
            Name = "Default Svelte Theme",
            Version = "0.1.0",
            ContractVersion = ThemeContractVersion.V1,
            Build = new ThemeBuildSpec { Command = "pnpm build", InstallCommand = "pnpm install" },
        };

        manifest.Id.Should().Be("default-svelte");
        manifest.ContractVersion.Should().Be("1.0");
        manifest.InputDir.Should().Be(".bocchi/input");
        manifest.OutputDir.Should().Be("build");
        manifest.Features.Posts.Should().BeTrue();
        manifest.Features.Photos.Should().BeFalse();
    }

    [Fact]
    public void ThemeConfigSchema_SupportsArchitectureFieldTypes()
    {
        var allTypes = Enum.GetValues<ThemeConfigFieldType>();
        allTypes.Should().Contain(new[]
        {
            ThemeConfigFieldType.String,
            ThemeConfigFieldType.Number,
            ThemeConfigFieldType.Boolean,
            ThemeConfigFieldType.Select,
            ThemeConfigFieldType.MultiSelect,
            ThemeConfigFieldType.Color,
            ThemeConfigFieldType.Image,
            ThemeConfigFieldType.Url,
            ThemeConfigFieldType.Group,
        });
    }

    [Fact]
    public void BuildContext_RoundTripsRequiredFields()
    {
        var ctx = new BuildContext
        {
            BuildTime = DateTimeOffset.UnixEpoch,
            BaseUrl = new Uri("https://example.invalid/"),
            ThemeId = "default-svelte",
            Environment = "development",
            Features = new ThemeFeatureFlags(),
        };

        ctx.BuildTime.Should().Be(DateTimeOffset.UnixEpoch);
        ctx.BaseUrl.Should().Be(new Uri("https://example.invalid/"));
        ctx.ThemeId.Should().Be("default-svelte");
    }
}
