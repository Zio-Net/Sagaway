using System.Text.Json.Nodes;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sagaway.Callback.Router;

internal class SagawayCallbackFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpRequest = context.HttpContext.Request;
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<ISagawayActor>>();
        var actorProxyFactory = context.HttpContext.RequestServices.GetRequiredService<IActorProxyFactory>();

        var payload = await JsonNode.ParseAsync(httpRequest.Body);
        if (payload is null)
        {
            logger.LogError("Payload is null or empty.");
            return await next(context);
        }

        var methodName = httpRequest.Headers["x-sagaway-dapr-callback-method-name"].FirstOrDefault();
        if (string.IsNullOrEmpty(methodName))
        {
            logger.LogError("x-sagaway-callback-method-name header is missing or empty.");
            return await next(context);
        }

        var actorId = httpRequest.Headers["x-sagaway-dapr-actor-id"].FirstOrDefault();
        if (string.IsNullOrEmpty(actorId))
        {
            logger.LogError("x-sagaway-dapr-actor-id header is missing or empty.");
            return await next(context);
        }

        var actorTypeName = httpRequest.Headers["x-sagaway-dapr-actor-type"].FirstOrDefault();
        if (string.IsNullOrEmpty(actorTypeName))
        {
            logger.LogError("x-sagaway-dapr-actor-type header is missing or empty.");
            return await next(context);
        }

        try
        {
            var proxy = actorProxyFactory.CreateActorProxy<ISagawayActor>(new ActorId(actorId), actorTypeName);
            await proxy.DispatchCallbackAsync(payload.ToJsonString(), methodName);
            logger.LogInformation("Dispatched callback to {ActorTypeName} for method {MethodName} with Actor ID {ActorId}", actorTypeName, methodName, actorId);

            return Results.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error dispatching callback to {ActorTypeName} for method {MethodName} with Actor ID {ActorId}", actorTypeName, methodName, actorId);
            return Results.Ok();
        }
    }
}