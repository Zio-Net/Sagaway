using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sagaway.Telemetry;

namespace Sagaway.OpenTelemetry;

public static class TelemetryExtensions
{
    /// <summary>
    /// Adds Saga OpenTelemetry services to the specified <see cref="IServiceCollection"/> with custom configuration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configureTracerProvider">An action to configure the OpenTelemetry TracerProviderBuilder.</param>
    /// <param name="appName">The name of the activity source for the saga operations.</param>
    /// <returns>The modified <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddSagawayOpenTelemetry(this IServiceCollection services,
        Action<TracerProviderBuilder> configureTracerProvider, 
        string appName)
    {
        var activitySourceName = $"{appName}.Sagaway";

        var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder();
        configureTracerProvider(tracerProviderBuilder);
        tracerProviderBuilder.AddSource(activitySourceName).SetResourceBuilder(
            ResourceBuilder.CreateDefault().AddService(activitySourceName));
        var tracerProvider = tracerProviderBuilder.Build();

        services.AddSingleton(tracerProvider);
        services.AddSingleton<ITelemetryAdapter, OpenTelemetryAdapter>(_ => new OpenTelemetryAdapter(activitySourceName));

        return services;
    }
}