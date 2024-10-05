namespace Sagaway.Callback.Propagator;

/// <summary>
/// Provides the callback binding metadata to add to a Dapr `InvokeBindingAsync` call, ensuring
/// that the callback is routed correctly. This interface should be used when returning a message to the caller service.
/// </summary>
public interface ICallbackBindingNameProvider
{
    /// <summary>
    /// Gets the callback binding name, which is used to route the response of the invoked Dapr binding.
    /// This binding name is critical for ensuring that the correct queue is used when the callback occurs.
    /// </summary>
    string? CallbackBindingName { get; }
}