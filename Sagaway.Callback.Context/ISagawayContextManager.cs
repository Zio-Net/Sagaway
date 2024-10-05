namespace Sagaway.Callback.Context;

public interface ISagawayContextManager
{
    /// <summary>
    /// the name of the key in the HTTP header that holds the base64-encoded context stack
    /// </summary>
    /// <returns>The key name of the compressed, serialized and decode base64 string</returns>
    string SagaWayContextHeaderKeyName { get; }

    /// <summary>
    /// Prepares the context for a downstream call, adding the new layer to the stack.
    /// </summary>
    /// <param name="currentContext">The current context that should be added to the call context stack.</param>
    /// <param name="optionalTargetContext">Add another context to serve as invocation target in the callee site,
    /// for example for a sub-saga proxy invocation</param>
    /// <returns>The key and value to add to the http header</returns>
    IReadOnlyDictionary<string, string> GetDownStreamCallContext(SagawayContext currentContext, SagawayContext? optionalTargetContext = null);

    /// <summary>
    /// Prepares the context for a callback to the upstream caller.
    /// </summary>
    /// <returns>The key and value to add to the http header</returns>
    IReadOnlyDictionary<string, string> GetUpStreamCallContext();

    /// <summary>
    /// Retrieves the current active context, which holds the information for the caller that called this service.
    /// </summary>
    /// <returns>The call context on the top of the class, the caller context</returns>
    SagawayContext? GetCallerContext();

    /// <summary>
    /// Sets the context from the incoming HTTP request by deserializing the base64-encoded context stack.
    /// </summary>
    void SetContextFromIncomingRequest(string base64ContextStack);
}
