namespace Bocchi.HomeServer.Security;

/// <summary>Setup 与 Admin 路径闸门中间件注册扩展。</summary>
public static class SetupGateExtensions
{
    /// <summary>启用 Bocchi Setup 闸门。</summary>
    public static IApplicationBuilder UseBocchiSetupGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<SetupGateMiddleware>();
    }

    /// <summary>启用 Bocchi Admin 路径闸门。</summary>
    public static IApplicationBuilder UseBocchiAdminGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<AdminGateMiddleware>();
    }
}
