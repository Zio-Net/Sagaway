using System.Text.Json.Nodes;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Sagaway.Callback.Router;

public static class WebApplicationSagawayExtension
{
    public static void UseSagawayCallbackRouter(this WebApplication app, string callbackBindingName, string actorTypeName)
    {
        app.MapPost("/" + callbackBindingName, async (
            HttpRequest httpRequest,
            [FromBody] JsonNode payload,
            [FromServices] IActorProxyFactory actorProxyFactory,
            [FromServices] ILogger<ISagawayActor> logger) =>
        {
           
            var methodName = httpRequest.Headers["x-sagaway-callback-method"].FirstOrDefault();
            if (string.IsNullOrEmpty(methodName))
            {
                logger.LogError("x-callback-method header is missing or empty.");
                return Results.Ok();
            }

            var actorId = httpRequest.Headers["x-sagaway-dapr-actor-id"].FirstOrDefault();
            if (string.IsNullOrEmpty(actorId))
            {
                logger.LogError("x-sagaway-dapr-actor-id header is missing or empty.");
                return Results.Ok();
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
        }).ExcludeFromDescription(); 
    }
}