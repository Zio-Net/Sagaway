using System.Linq.Expressions;
using Dapr.Actors;

namespace Sagaway.Callback.Router;

public interface ISagawayActor : IActor
{
    /// <summary>
    /// Dispatch the callback to the actor
    /// </summary>
    /// <param name="payloadJson">Holds the parameter for the target function</param>
    /// <param name="methodName">Holds the target method</param>
    /// <returns>An async task</returns>
    Task DispatchCallbackAsync(string payloadJson, string methodName);
}