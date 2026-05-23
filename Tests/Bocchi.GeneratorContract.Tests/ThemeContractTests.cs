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
        manifest.InputDir.Should().Be("../../cache/theme-input");
        manifest.OutputDir.Should().Be("build");
        manifest.Features.Posts.Should().BeTrue();
        manifest.Features.Photos.Should().BeFalse();
    }

    [Fact]
    public void ThemeManifest_SupportsRunnerSpecWithoutLegacyBuildCommand()
    {
        var manifest = new ThemeManifest
        {
            Id = "default-static",
            Name = "Bocchi Mono",
            Version = "0.1.0",
            ContractVersion = ThemeContractVersion.V1,
            Runner = new ThemeRunnerSpec
            {
                Kind = "fluid-static",
                Entry = "fluid",
            },
        };

        manifest.Runner.Should().NotBeNull();
        manifest.Runner!.Kind.Should().Be("fluid-static");
        manifest.Runner.Entry.Should().Be("fluid");
        manifest.Build.Should().BeNull();
    }

    [Fact]
    public void ThemeManifest_SupportsPrivateI18nKeyDeclarations()
    {
        var manifest = new ThemeManifest
        {
            Id = "default-static",
            Name = "Bocchi Mono",
            Version = "0.1.0",
            ContractVersion = ThemeContractVersion.V1,
            I18n = new ThemeI18nManifest
            {
                SupportedLanguages = ["en-US", "zh-CN"],
                DefaultLanguage = "en-US",
                Keys =
                [
                    new ThemeI18nKeyManifest
                    {
                        Key = "theme.defaultStatic.colophonBuiltWith",
                        Title = "Colophon built-with text",
                        Description = "Footer text used by Bocchi Mono.",
                        DefaultValues = new Dictionary<string, string>
                        {
                            ["en-US"] = "Built with Bocchi.",
                            ["zh-CN"] = "由 Bocchi 构建。",
                        },
                    },
                ],
            },
        };

        manifest.I18n.Should().NotBeNull();
        manifest.I18n!.SupportedLanguages.Should().Contain(["en-US", "zh-CN"]);
        manifest.I18n.Keys.Should().ContainSingle(key =>
            key.Key == "theme.defaultStatic.colophonBuiltWith"
            && key.DefaultValues["zh-CN"] == "由 Bocchi 构建。");
    }

    [Fact]
    public void ThemeManifest_SupportsPageTemplatesAndSpecialPages()
    {
        var manifest = new ThemeManifest
        {
            Id = "custom-theme",
            Name = "Custom Theme",
            Version = "0.1.0",
            ContractVersion = ThemeContractVersion.V1,
            PageTemplates =
            [
                new ThemePageTemplateManifest
                {
                    Name = "normal",
                    DisplayName = "i18n://theme@theme.custom.pageTemplate.normal",
                },
            ],
            SpecialPages =
            [
                new ThemeSpecialPageManifest
                {
                    Name = "calculator",
                    DisplayName = "Calculator",
                    Route = "/calculator/",
                },
            ],
        };

        manifest.PageTemplates.Should().ContainSingle(template =>
            template.Name == "normal" &&
            template.DisplayName == "i18n://theme@theme.custom.pageTemplate.normal");
        manifest.SpecialPages.Should().ContainSingle(page =>
            page.Name == "calculator" &&
            page.Route == "/calculator/");
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
            ThemeConfigFieldType.LocalizedText,
            ThemeConfigFieldType.LocalizedTextList,
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
