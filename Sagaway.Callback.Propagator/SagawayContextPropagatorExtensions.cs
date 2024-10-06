using Dapr.Client;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sagaway.Callback.Context;

namespace Sagaway.Callback.Propagator;

/// <summary>
/// Extension methods for adding and configuring Sagaway context propagation within a Dapr-based system.
/// </summary>
public static class SagawayContextPropagatorExtensions
{
    // ReSharper disable once UnusedMethodReturnValue.Global
    /// <summary>
    /// Adds middleware to the application pipeline to handle propagation of Sagaway context via HTTP headers.
    /// </summary>
    /// <param name="builder">The application builder to add the middleware to.</param>
    /// <returns>The modified application builder.</returns>
    public static IApplicationBuilder UseSagawayContextPropagator(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HeaderPropagationMiddleware>();
    }

    // ReSharper disable once UnusedMethodReturnValue.Global
    // ReSharper disable once UnusedMember.Global
    /// <summary>
    /// Adds Dapr integration and Sagaway context propagation capabilities to the service collection.
    /// This includes setting up context managers, metadata providers, and dynamic callback invokers.
    /// </summary>
    /// <param name="services">The service collection to add the context propagators to.</param>
    /// <param name="httpClientProvider">Optional function to provide a custom <see cref="HttpClient"/> factory.</param>
    /// <returns>The MVC builder for chaining additional service configurations.</returns>
    public static IServiceCollection AddDaprWithSagawayContextPropagator(this IServiceCollection services,
        Func<HttpMessageHandler, HttpClient>? httpClientProvider = null)
    {
        services.AddHttpContextAccessor();

        services.AddScoped<ISagawayContextManager, SagawayContextManager>();

        //add the caller id provider if the user had not provided one
        services.TryAddScoped<ISagawayCallerIdProvider, SagawayCallerIdProvider>();
        
        services.AddScoped<ISagawayCallbackMetadataProvider, SagawayCallbackMetadataProvider>();

        // Conditionally register ISagawayCallbackInvoker using dynamic to avoid direct package dependency
        RegisterCallbackInvokerIfExists(services);


        //for backward compatibility
        services.AddScoped<ICallbackBindingNameProvider>(provider =>
        {
            var callbackMetadataProvider = provider.GetRequiredService<ISagawayCallbackMetadataProvider>();
            return callbackMetadataProvider;
        });

        services.AddScoped<ISagawayContextInformationProvider>(provider =>
        {
            var callbackMetadataProvider = provider.GetRequiredService<ISagawayCallbackMetadataProvider>();
            return (callbackMetadataProvider as ISagawayContextInformationProvider)!;
        });

        services.AddDaprClient((provider, daprClientBuilder) =>
            AddSagawayContextPropagator(daprClientBuilder, provider, httpClientProvider));

        return services;
    }

    // ReSharper disable once UnusedMethodReturnValue.Global
    /// <summary>
    /// Adds Sagaway context propagation to Dapr's gRPC channel options by wrapping the <see cref="HttpClient"/>
    /// with a custom message handler that propagates the context.
    /// </summary>
    /// <param name="builder">The Dapr client builder.</param>
    /// <param name="serviceProvider">The services provider</param>
    /// <param name="httpClientProvider">Optional function to provide a custom <see cref="HttpClient"/> factory.</param>
    private static void AddSagawayContextPropagator(
        DaprClientBuilder builder,
        IServiceProvider serviceProvider,
        Func<HttpMessageHandler, HttpClient>? httpClientProvider = null)
    {
        // If user has provided a custom HttpClient factory, wrap it in SagawayContextPropagationHandler
        httpClientProvider ??= innerHandler =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<SagawayContextPropagationHandler>>();

            // Wrap the provided HttpMessageHandler (or create default) with SagawayContextPropagationHandler
            var propagationHandler = new SagawayContextPropagationHandler(serviceProvider, logger)
            {
                InnerHandler = innerHandler // Use the user's provided handler or a default one
            };

            return new HttpClient(propagationHandler);
        };

        var jsonOptions = serviceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;
        builder.UseJsonSerializationOptions(jsonOptions.SerializerOptions);

        // Configure Dapr's gRPC channel options to use the wrapped HttpClient
        builder.UseGrpcChannelOptions(new GrpcChannelOptions
        {
            HttpClient = httpClientProvider(new HttpClientHandler())
        });
    }

    /// <summary>
    /// Dynamically registers the `ISagawayCallbackInvoker` and its implementation if they exist
    /// in the loaded assemblies. This avoids direct package dependency while allowing the invoker to be registered.
    /// </summary>
    /// <param name="services">The service collection to add the callback invoker to.</param>
    private static void RegisterCallbackInvokerIfExists(IServiceCollection services)
    {
        // Load types dynamically using AppDomain
        var callbackInvokerType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "ISagawayCallbackInvoker");

        var callbackInvokerImplType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "SagaWayCallbackInvoker");

        if (callbackInvokerType != null && callbackInvokerImplType != null)
        {
            // Use reflection to call TryAddScoped<TService, TImplementation>
            var method = typeof(ServiceCollectionDescriptorExtensions)
                .GetMethod(nameof(ServiceCollectionDescriptorExtensions.TryAddScoped), [typeof(IServiceCollection), typeof(Type), typeof(Type)
                ]);

            if (method != null)
            {
                method.Invoke(null, [services, callbackInvokerType, callbackInvokerImplType]);
            }
        }
    }

}