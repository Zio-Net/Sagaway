using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Sagaway.Telemetry;
using System;
using OpenTelemetry;
using Sagaway.OpenTelemetry;

public static class TelemetryExtensions
{
    /// <summary>
    /// Adds Saga OpenTelemetry services to the specified <see cref="IServiceCollection"/> with custom configuration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configureTracerProvider">An action to configure the OpenTelemetry TracerProviderBuilder.</param>
    /// <param name="activitySourceName">The name of the activity source for the saga operations.</param>
    /// <returns>The modified <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddSagaOpenTelemetry(this IServiceCollection services, Action<TracerProviderBuilder> configureTracerProvider, string appName)
    {
        // Configure and build TracerProvider
        var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder();
        configureTracerProvider(tracerProviderBuilder);
        tracerProviderBuilder.Build();

        // Register the OpenTelemetryAdapter with the activity source name
        services.AddSingleton<ITelemetryAdapter>(provider => new OpenTelemetryAdapter($"{appName}.Sagaway"));

        return services;
    }
}