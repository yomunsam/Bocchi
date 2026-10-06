using Bocchi.Workspace;

namespace Bocchi.HomeServer.Hosting;

/// <summary>
/// 解析 Home Server 的 DataRoot。生产环境必须显式配置绝对路径：
/// 不再默认放在程序目录下，避免升级时整目录替换把数据库、密钥和 workspace 一起删掉。
/// </summary>
public static class DataRootResolver
{
    /// <summary>DataRoot 配置键（环境变量写作 <c>Bocchi__DataRoot</c>）。</summary>
    public const string ConfigurationKey = $"{BocchiDataOptions.SectionName}:DataRoot";

    /// <summary>开发环境未配置时使用的目录名，相对项目目录，已被 .gitignore 忽略。</summary>
    public const string DevelopmentDefault = ".bocchi-dev-data";

    /// <summary>返回 DataRoot 的绝对路径；生产环境未配置或配置为相对路径时抛出 <see cref="BocchiStartupException"/>。</summary>
    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configured = configuration[ConfigurationKey]?.Trim();
        if (environment.IsDevelopment())
        {
            // 开发期默认值避开源码目录里的 Data/，防止大小写不敏感文件系统把 data/ 合并进去。
            return Path.GetFullPath(Path.Combine(environment.ContentRootPath,
                string.IsNullOrEmpty(configured) ? DevelopmentDefault : configured));
        }

        if (string.IsNullOrEmpty(configured))
        {
            throw new BocchiStartupException(
                "未配置数据目录。请设置环境变量 Bocchi__DataRoot（或配置项 Bocchi:DataRoot）为一个绝对路径，"
                + "例如 Bocchi__DataRoot=/var/lib/bocchi。Docker 镜像默认使用 /data。");
        }

        if (!Path.IsPathRooted(configured))
        {
            throw new BocchiStartupException(
                $"数据目录必须是绝对路径，当前为 '{configured}'。请把 Bocchi__DataRoot 改成绝对路径。");
        }

        return Path.GetFullPath(configured);
    }
}
