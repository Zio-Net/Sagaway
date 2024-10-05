using Sagaway.Callback.Context;

namespace Sagaway.Callback.Router;

/// <summary>
/// Interface for invoking callbacks in the Sagaway framework.
/// This interface defines a contract for dispatching callbacks asynchronously to the appropriate service or actor
/// based on the provided Sagaway context.
/// </summary>
public interface ISagawayCallbackInvoker
{
    /// <summary>
    /// Dispatches a callback asynchronously to the specified service or actor based on the provided 
    /// <paramref name="sagawayContext"/>. The payload is passed in as a JSON string.
    /// </summary>
    /// <param name="payloadJson">The payload for the callback in JSON format.</param>
    /// <param name="sagawayContext">The context that contains details about the caller and callback information.</param>
    /// <returns>A task that represents the asynchronous operation of dispatching the callback.</returns>
    /// <remarks>
    /// The method uses the context to determine whether the callback should be routed to an actor or 
    /// to a regular service. The context provides information like the caller ID, the method to be invoked, 
    /// and the service or actor type.
    /// </remarks>
    Task DispatchCallbackAsync(string payloadJson, SagawayContext sagawayContext);
}