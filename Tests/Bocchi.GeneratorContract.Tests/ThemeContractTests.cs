namespace Bocchi.GeneratorContract.Tests;

public sealed class ThemeContractTests
{
    /// <summary>Theme package validation 同等 JSON 配置，复用以避免测试里重复创建 serializer options。</summary>
    private static readonly System.Text.Json.JsonSerializerOptions SchemaJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

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
            Id = "bocchi-mono",
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
            Id = "bocchi-mono",
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
                        Key = "theme.bocchi-mono.colophonBuiltWith",
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
            key.Key == "theme.bocchi-mono.colophonBuiltWith"
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
    public void ThemeManifest_SupportsStaticAssets()
    {
        var manifest = new ThemeManifest
        {
            Id = "asset-theme",
            Name = "Asset Theme",
            Version = "0.1.0",
            ContractVersion = ThemeContractVersion.V1,
            StaticAssets =
            [
                new ThemeStaticAssetManifest
                {
                    From = "assets",
                    To = "/assets",
                    Include = ["**/*.min.css", "**/*.min.js"],
                    Exclude = ["**/*.map"],
                },
            ],
        };

        manifest.StaticAssets.Should().ContainSingle(asset =>
            asset.From == "assets" &&
            asset.To == "/assets" &&
            asset.Include.Contains("**/*.min.css") &&
            asset.Exclude.Contains("**/*.map"));
    }

    [Fact]
    public void ThemeManifest_StaticAssetsDefaultGlobsDeserialize()
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ThemeManifest>(
            """
            {
              "id": "asset-theme",
              "name": "Asset Theme",
              "version": "0.1.0",
              "contractVersion": "1.0",
              "staticAssets": [
                {
                  "from": "assets",
                  "to": "/assets"
                }
              ]
            }
            """,
            SchemaJsonOptions);

        manifest.Should().NotBeNull();
        var asset = manifest!.StaticAssets.Should().ContainSingle().Subject;
        asset.Include.Should().ContainSingle("**/*");
        asset.Exclude.Should().BeEmpty();
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
    public void ThemeConfigField_CanDeclareInlineTextFormat()
    {
        var field = new ThemeConfigField
        {
            Key = "home.heroTitle",
            Type = ThemeConfigFieldType.LocalizedText,
            Title = "Home hero title",
            TextFormat = "inlineColor",
        };

        field.TextFormat.Should().Be("inlineColor");
    }

    [Fact]
    public void ThemeConfigSchema_DeserializesLegacyStringOptions()
    {
        var schema = DeserializeThemeConfigSchema("""
            {
              "groups": [
                {
                  "id": "reading",
                  "title": "Reading",
                  "fields": [
                    {
                      "key": "reading.timeZoneDisplayStyle",
                      "type": "select",
                      "title": "Time zone display",
                      "options": ["utcOffset", "ianaTimeZone"]
                    }
                  ]
                }
              ]
            }
            """);

        var field = schema.Groups.Single().Fields.Single();
        field.Type.Should().Be(ThemeConfigFieldType.Select);
        field.Options.Should().NotBeNull();
        field.Options!.Select(option => (option.Value, option.Label))
            .Should().Equal(("utcOffset", "utcOffset"), ("ianaTimeZone", "ianaTimeZone"));
    }

    [Fact]
    public void ThemeConfigSchema_DeserializesValueLabelOptions()
    {
        var schema = DeserializeThemeConfigSchema("""
            {
              "groups": [
                {
                  "id": "reading",
                  "title": "Reading",
                  "fields": [
                    {
                      "key": "reading.timeZoneDisplayStyle",
                      "type": "select",
                      "title": "Time zone display",
                      "options": [
                        { "value": "utcOffset", "label": "UTC offset（UTC+8）" },
                        { "value": "ianaTimeZone", "label": "IANA time zone（Asia/Shanghai）" }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        var field = schema.Groups.Single().Fields.Single();
        field.Type.Should().Be(ThemeConfigFieldType.Select);
        field.Options.Should().NotBeNull();
        field.Options!.Select(option => (option.Value, option.Label))
            .Should().Equal(
                ("utcOffset", "UTC offset（UTC+8）"),
                ("ianaTimeZone", "IANA time zone（Asia/Shanghai）"));
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

    /// <summary>使用 Theme package validation 同等 JSON 配置验证 Contract DTO 的解析边界。</summary>
    private static ThemeConfigSchema DeserializeThemeConfigSchema(string json)
        => System.Text.Json.JsonSerializer.Deserialize<ThemeConfigSchema>(json, SchemaJsonOptions)!;
}
