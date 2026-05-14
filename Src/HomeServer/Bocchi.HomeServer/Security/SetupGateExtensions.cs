namespace Bocchi.HomeServer.Security;

/// <summary>Setup 闸门中间件注册扩展。</summary>
public static class SetupGateExtensions
{
    /// <summary>启用 Bocchi Setup 闸门。</summary>
    public static IApplicationBuilder UseBocchiSetupGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<SetupGateMiddleware>();
    }
}
