namespace Sagaway.Callback.Propagator;

/// <summary>
/// Provides the required HTTP headers for the callback when using Dapr `InvokeBindingAsync`.
/// This metadata is used to ensure the callback is routed correctly and contains all the necessary context
/// information. The dictionary returned by these methods is used as metadata, which Dapr automatically converts
/// into HTTP headers with the appropriate context information.
/// </summary>
/// <remarks>
/// This interface is intended for use when the caller service is running as a local service registered in the DI container,
/// rather than as an actor. For actor-based services, the Actor Host provides a similar method that also includes the ActorId.
/// The service handling the callback must be registered in the DI container.
/// </remarks>
public interface ISagawayCallbackMetadataProvider : ICallbackBindingNameProvider
{
    /// <summary>
    /// Gets the callback metadata, including the necessary headers, to ensure proper routing and context.
    /// This method is used when invoking a downstream service, providing context to ensure the callee service
    /// can call back to the caller.
    /// </summary>
    /// <param name="callbackMethodName">The name of the method that should be called upon the callback.</param>
    /// <param name="registeredServiceType">The type of the registered service in the DI container.</param>
    /// <param name="daprBindingName">The Dapr binding name that will be used by the callee service to return the call.</param>
    /// <returns>A dictionary of headers (as metadata) for the callback.</returns>
    IReadOnlyDictionary<string, string> GetCallbackMetadata(
        string callbackMethodName, Type registeredServiceType, string daprBindingName);

    /// <summary>
    /// Gets the callback metadata, including custom metadata, to ensure proper routing and context.
    /// This method is used when invoking a downstream service, providing additional custom metadata 
    /// along with the context, so the callee service can call back to the caller.
    /// </summary>
    /// <typeparam name="TMetadata">The type of the custom metadata to be provided in the callback.</typeparam>
    /// <param name="callbackMethodName">The name of the method that should be called upon the callback.</param>
    /// <param name="registeredServiceType">The type of the registered service in the DI container.</param>
    /// <param name="daprBindingName">The Dapr binding name that will be invoked when calling back.</param>
    /// <param name="customMetadata">Custom metadata that needs to be included in the callback context.</param>
    /// <returns>A dictionary of headers (as metadata) for the callback, including custom metadata.</returns>
    IReadOnlyDictionary<string, string> GetCallbackMetadata<TMetadata>(
        string callbackMethodName, Type registeredServiceType, string daprBindingName, TMetadata customMetadata);
}