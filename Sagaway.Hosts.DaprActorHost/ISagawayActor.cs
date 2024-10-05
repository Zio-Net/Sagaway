using Dapr.Actors;

namespace Sagaway.Hosts.DaprActorHost;

/// <summary>
/// Interface for a Sagaway actor, extending the Dapr <see cref="IActor"/> interface.
/// It defines the ability to dispatch a callback to an actor method based on the provided payload and method name.
/// </summary>
public interface ISagawayActor : IActor
{
    /// <summary>
    /// Dispatches a callback to the actor by invoking the specified method with the provided payload.
    /// </summary>
    /// <param name="payloadJson">The JSON-serialized string containing the parameters for the target method.</param>
    /// <param name="methodName">The name of the method to invoke on the actor.</param>
    /// <returns>A task representing the asynchronous operation of dispatching the callback.</returns>
    Task DispatchCallbackAsync(string payloadJson, string methodName);
}