using Microsoft.AspNetCore.Builder;

namespace Sagaway.Hosts.DaprActorHost;

/// <summary>
/// Extension methods for configuring a <see cref="WebApplication"/> to work with Sagaway actors.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps the Sagaway actor handlers to the specified <see cref="WebApplication"/>.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication"/> to map the handlers to.</param>
    public static void MapSagawayActorsHandlers(this WebApplication app)
    {
        // Register the custom middleware
        app.UseMiddleware<ActorDeactivationMiddleware>();

        // Register Dapr actor endpoints
        app.MapActorsHandlers();
    }
}
