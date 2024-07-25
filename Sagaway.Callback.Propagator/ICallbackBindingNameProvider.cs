namespace Sagaway.Callback.Propagator;

/// <summary>
/// Provide the callback binding name as received from the request headers
/// </summary>
public interface ICallbackBindingNameProvider
{
    string? CallbackBindingName { get; }
}