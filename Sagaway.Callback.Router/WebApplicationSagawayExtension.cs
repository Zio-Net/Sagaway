using System.Text.Json.Nodes;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Polly;

namespace Sagaway.Callback.Router;

public static class WebApplicationSagawayExtension
{
    public static RouteHandlerBuilder UseSagawayCallbackRouter(this WebApplication app, string callbackBindingName)
    {
        return app.MapPost("/" + callbackBindingName, async (
            HttpRequest httpRequest,
            [FromBody] JsonNode payload,
            [FromServices] IActorProxyFactory actorProxyFactory,
            [FromServices] ILogger<ISagawayActor> logger) =>
        {

            var methodName = httpRequest.Headers["x-sagaway-dapr-callback-method-name"].FirstOrDefault();
            if (string.IsNullOrEmpty(methodName))
            {
                logger.LogError("x-sagaway-callback-method-name header is missing or empty.");
                return Results.Ok();
            }

            var actorId = httpRequest.Headers["x-sagaway-dapr-actor-id"].FirstOrDefault();
            if (string.IsNullOrEmpty(actorId))
            {
                logger.LogError("x-sagaway-dapr-actor-id header is missing or empty.");
                return Results.Ok();
            }

            var actorTypeName = httpRequest.Headers["x-sagaway-dapr-actor-type"].FirstOrDefault();
            if (string.IsNullOrEmpty(actorTypeName))
            {
                logger.LogError("x-sagaway-dapr-actor-type header is missing or empty.");
                return Results.Ok();
            }

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(4, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Min(Math.Pow(1, retryAttempt), 30)) +
                    TimeSpan.FromMilliseconds(new Random().Next(0, 1000)),
                (exception, timeSpan, retryCount, context) =>
                {
                    logger.LogWarning(exception, "Retry {RetryCount} for method {MethodName} on actor {ActorTypeName} with ID {ActorId} failed. Retrying in {Delay} seconds...",
                                      retryCount, methodName, actorTypeName, actorId, timeSpan.TotalSeconds);
                });

            try
            {
                var proxy = actorProxyFactory.CreateActorProxy<ISagawayActor>(new ActorId(actorId), actorTypeName);
                await retryPolicy.ExecuteAsync(async () =>
                {
                    await proxy.DispatchCallbackAsync(payload.ToJsonString(), methodName);
                });

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


    public static RouteHandlerBuilder UseSagawayCallbackRouter(this WebApplication app, string callbackBindingName, Delegate handler)
    {
        return app.MapPost("/" + callbackBindingName, handler)
        .AddEndpointFilter<SagawayCallbackFilter>();
    }
}