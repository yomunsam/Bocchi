using System.Globalization;
using System.Text.Json;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Dashboard 自身的 JSON 文案读取服务。它只服务 Admin UI，不把语言偏好传给前台 Theme。
/// </summary>
public sealed class DashboardLocalizationService
{
    /// <summary>当前 Dashboard UI 的默认语言；沿用 M4 已有英文界面，避免升级后突然改变后台文案。</summary>
    public const string DefaultLanguageCode = "en-US";

    /// <summary>Dashboard 首批支持的 UI 语言。控件使用列表渲染，后续增加语言无需改变交互形态。</summary>
    public static IReadOnlyList<LanguageRecord> SupportedDashboardLanguages { get; } =
    [
        new() { Code = "en-US", NativeName = "English", EnglishName = "English" },
        new() { Code = "zh-CN", NativeName = "简体中文", EnglishName = "Simplified Chinese" },
    ];

    /// <summary>Dashboard JSON 文案使用 Web 默认 JSON 选项，便于后续保持 camelCase 资源结构。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>应用内容根，用于开发期读取 JSON 资源。</summary>
    private readonly IWebHostEnvironment _environment;

    /// <summary>缺失资源文件的诊断日志。</summary>
    private readonly ILogger<DashboardLocalizationService> _logger;

    /// <summary>按需加载并缓存所有 Dashboard 文案，避免每次渲染重复读文件。</summary>
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _resources;

    /// <summary>构造 Dashboard JSON 文案服务。</summary>
    public DashboardLocalizationService(
        IWebHostEnvironment environment,
        ILogger<DashboardLocalizationService> logger)
    {
        _environment = environment;
        _logger = logger;
        _resources = new Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(LoadResources);
    }

    /// <summary>当前请求使用的 Dashboard UI 语言。</summary>
    public LanguageRecord CurrentLanguage => ResolveLanguage(CultureInfo.CurrentUICulture.Name);

    /// <summary>按当前请求语言读取文案；缺失时回退到默认语言，再回退到 key 本身。</summary>
    public string this[string key] => Get(key);

    /// <summary>按指定语言读取文案；语言不受支持时使用默认语言。</summary>
    public string Get(string key, string? languageCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var language = ResolveLanguage(languageCode ?? CultureInfo.CurrentUICulture.Name);
        var resources = _resources.Value;
        if (resources.TryGetValue(language.Code, out var selected)
            && selected.TryGetValue(key, out var value))
        {
            return value;
        }

        if (resources.TryGetValue(DefaultLanguageCode, out var fallback)
            && fallback.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    /// <summary>把输入语言代码收束到 Dashboard 支持的语言；未知值回退到默认语言。</summary>
    public LanguageRecord ResolveLanguage(string? languageCode)
    {
        var requested = NormalizeCode(languageCode);
        return SupportedDashboardLanguages.FirstOrDefault(
                x => string.Equals(x.Code, requested, StringComparison.OrdinalIgnoreCase))
            ?? SupportedDashboardLanguages.First(x => x.Code == DefaultLanguageCode);
    }

    /// <summary>从项目内容根或发布输出目录读取 Dashboard JSON 文案。</summary>
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadResources()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in SupportedDashboardLanguages)
        {
            var path = ResolveResourcePath(language.Code);
            if (!File.Exists(path))
            {
                _logger.LogWarning("Dashboard localization resource '{ResourcePath}' was not found.", path);
                result[language.Code] = new Dictionary<string, string>(StringComparer.Ordinal);
                continue;
            }

            using var stream = File.OpenRead(path);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
            result[language.Code] = values;
        }

        return result;
    }

    /// <summary>解析单个语言资源文件路径，兼容开发期内容根与发布后输出目录。</summary>
    private string ResolveResourcePath(string languageCode)
    {
        var relative = Path.Combine("Localization", "Dashboard", languageCode + ".json");
        var contentRootPath = Path.Combine(_environment.ContentRootPath, relative);
        if (File.Exists(contentRootPath))
        {
            return contentRootPath;
        }

        return Path.Combine(AppContext.BaseDirectory, relative);
    }

    /// <summary>标准化 Dashboard UI language 输入，空值回退到默认语言。</summary>
    private static string NormalizeCode(string? languageCode)
        => string.IsNullOrWhiteSpace(languageCode)
            ? DefaultLanguageCode
            : languageCode.Trim();
}
