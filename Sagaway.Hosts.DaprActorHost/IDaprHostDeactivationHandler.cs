namespace Sagaway.Hosts.DaprActorHost;

/// <summary>
/// Serves as an endpoint for the middleware to inform an actor deactivation before the state is no longer available
/// </summary>
public interface IDaprHostDeactivationHandler : Dapr.Actors.IActor
{
    /// <summary>
    /// Inform the host that its actor is about to be deactivated
    /// </summary>
    /// <returns></returns>
    Task OnDeactivateActorAsync();

    /// <summary>
    /// Inform the host that the deactivation middleware has been registered
    /// </summary>
    Task InformDeactivationMiddlewareRegisteredAsync();
}