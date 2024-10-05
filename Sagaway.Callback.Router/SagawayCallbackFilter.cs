using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sagaway.Callback.Context;

namespace Sagaway.Callback.Router;

/// <summary>
/// Represents an endpoint filter that processes incoming callback requests for the Sagaway framework.
/// This filter manages the propagation of the Sagaway context and dispatches callbacks to the appropriate service or actor.
/// </summary>
internal class SagawayCallbackFilter : IEndpointFilter
{
    /// <summary>
    /// Invokes the callback filter asynchronously, handling the propagation of the Sagaway context and dispatching the callback
    /// based on the provided context and headers.
    /// </summary>
    /// <param name="context">The invocation context, containing the arguments and request details.</param>
    /// <param name="next">The delegate representing the next filter in the pipeline or the final endpoint handler.</param>
    /// <returns>A task representing the asynchronous operation that may return a result.</returns>
    /// <remarks>
    /// This filter extracts the Sagaway context from the request headers and uses the <see cref="ProxyInvoker"/>
    /// to invoke the appropriate service or actor based on the context. It uses dependency injection to retrieve required
    /// services such as <see cref="ISagawayContextManager"/> and <see cref="ISagawayCallbackInvoker"/>.
    /// </remarks>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpRequest = context.HttpContext.Request;
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<SagawayCallbackFilter>>();
        var serviceProvider = context.HttpContext.RequestServices;
        var callerIdProvider = context.HttpContext.RequestServices.GetRequiredService<ISagawayCallerIdProvider>();

        var sagawayContextManager = serviceProvider.GetRequiredService<ISagawayContextManager>();

        var payload = context.Arguments.OfType<JsonNode>().FirstOrDefault();

        ProxyInvoker proxyInvoker = new(logger, sagawayContextManager, callerIdProvider, serviceProvider.GetRequiredService<ISagawayCallbackInvoker>());
        
        return await proxyInvoker.InvokeAsync(payload, async () => await next(context), httpRequest.Headers);
    }
}