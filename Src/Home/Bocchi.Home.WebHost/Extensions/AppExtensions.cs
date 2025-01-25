namespace Bocchi.Home.WebHost.Extensions;

public static class AppExtensions
{
    public static bool IsContainer(this IHostEnvironment env)
    {
        return env.IsEnvironment("DOTNET_RUNNING_IN_CONTAINER");
    }
}