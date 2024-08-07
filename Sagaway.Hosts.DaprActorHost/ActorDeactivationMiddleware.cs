using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Sagaway.Hosts.DaprActorHost;

public class ActorDeactivationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ActorDeactivationMiddleware> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ActorDeactivationMiddleware(RequestDelegate next, ILogger<ActorDeactivationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            if (context.Request.Method == HttpMethods.Delete && context.Request.Path.StartsWithSegments("/actors"))
            {
                var (actorProxy, actorTypeName, actorId) = GetActorProxy(context);

                if (actorProxy == null)
                {
                    _logger.LogWarning(
                        "Could not get actor proxy from the request. Actor[{actorTypeName},{actorId}] deactivation will not be informed.",
                        actorTypeName ?? "null", actorId ?? "null");
                    return;
                }


                await actorProxy.OnDeactivateActorAsync();

                _logger.LogInformation(
                    "Called OnDeactivateActorAsync for ActorType: {ActorType}, ActorId: {ActorId}",
                    actorTypeName, actorId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the actor deactivation request.");
        }
        finally
        {
            // Continue with the next middleware in the pipeline    
            await _next(context);
        }
    }

    private (IDaprHostDeactivationHandler? proxy, string? actorTypeName, string? actorId) GetActorProxy(
        HttpContext context)
    {
        var routeValues = context.Request.RouteValues;
        if (!routeValues.TryGetValue("actorTypeName", out var actorTypeNameObj) ||
            !routeValues.TryGetValue("actorId", out var actorIdObj))
            return (null, null, null);

        var actorTypeName = actorTypeNameObj?.ToString();
        var actorId = actorIdObj?.ToString();

        if (string.IsNullOrEmpty(actorTypeName) || string.IsNullOrEmpty(actorId)) 
            return (null, null, null);

        _logger.LogInformation(
            "Intercepted actor request for ActorType: {ActorType}, ActorId: {ActorId}",
            actorTypeName, actorId);

        var actorIdInstance = new ActorId(actorId);
        var actorProxy =
            ActorProxy.Create<IDaprHostDeactivationHandler>(actorIdInstance, actorTypeName);

        return (actorProxy, actorTypeName, actorId);
    }
}