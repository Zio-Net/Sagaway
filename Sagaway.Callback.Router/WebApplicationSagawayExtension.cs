using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sagaway.Callback.Context;

namespace Sagaway.Callback.Router;

/// <summary>
/// Provides extension methods for configuring the Sagaway Callback Router in an ASP.NET Core application.
/// </summary>
public static class WebApplicationSagawayExtension
{
    // ReSharper disable once UnusedMethodReturnValue.Global
    /// <summary>
    /// Registers a POST route that listens for a callback request for the given binding name and routes the request
    /// to the appropriate service or actor.
    /// </summary>
    /// <param name="app">The ASP.NET Core web application instance.</param>
    /// <param name="callbackBindingName">The name of the binding used to receive callbacks.</param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> representing the configured route.</returns>
    /// <remarks>
    /// This method sets up a POST route using the provided callback binding name and automatically handles the routing of callback requests.
    /// It uses the provided Sagaway services to manage context propagation and callback invocation.
    /// </remarks>
    public static RouteHandlerBuilder UseSagawayCallbackRouter(this WebApplication app, string callbackBindingName)
    {
        return app.MapPost("/" + callbackBindingName, async (
            HttpRequest httpRequest,
            [FromBody] JsonNode payload,
            [FromServices] ISagawayContextManager sagawayContextManager,
            [FromServices] ISagawayCallerIdProvider callerIdProvider,
            [FromServices] IServiceProvider serviceProvider,
            [FromServices] ILogger<ISagawayCallerIdProvider> logger) =>
        {
            ProxyInvoker proxyInvoker = new(logger, sagawayContextManager, callerIdProvider, serviceProvider.GetRequiredService<ISagawayCallbackInvoker>());

            return await proxyInvoker.InvokeAsync(payload, () => Task.FromResult<object?>(Results.Ok()), httpRequest.Headers);
        }).ExcludeFromDescription();
    }

    // ReSharper disable once UnusedMember.Global
    /// <summary>
    /// Registers a POST route with a custom handler that listens for a callback request for the given binding name and routes the request
    /// to the specified delegate.
    /// </summary>
    /// <param name="app">The ASP.NET Core web application instance.</param>
    /// <param name="callbackBindingName">The name of the binding used to receive callbacks.</param>
    /// <param name="handler">The custom delegate that will handle the callback request.</param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> representing the configured route.</returns>
    /// <remarks>
    /// This method allows more flexibility by letting the caller provide a custom delegate to handle the callback request. 
    /// The callback filter is applied automatically to manage context propagation.
    /// </remarks>
    public static RouteHandlerBuilder UseSagawayCallbackRouter(this WebApplication app, string callbackBindingName, Delegate handler)
    {
        return app.MapPost("/" + callbackBindingName, handler)
        .AddEndpointFilter<SagawayCallbackFilter>();
    }
}