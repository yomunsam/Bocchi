namespace Bocchi.HomeServer.Data;

/// <summary>
/// Home Server 业务角色名。M4 只开放一个业务角色：Admin。
/// </summary>
public static class BocchiRoleNames
{
    /// <summary>可以进入 Dashboard、管理内容、设置、构建和用户的管理员角色。</summary>
    public const string Admin = "Admin";
}
