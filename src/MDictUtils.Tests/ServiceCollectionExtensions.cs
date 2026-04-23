using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Tests;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTestLogging(this IServiceCollection services)
        => services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });
}
