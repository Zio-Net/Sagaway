using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Json;

namespace Sagaway.Callback.Propagator;

public static class JsonOptionsExtensions
{
    public static IServiceCollection AddJsonOptions(this IServiceCollection services, Action<JsonOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services;
    }
}

