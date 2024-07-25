namespace Sagaway.Callback.Propagator;

/// <summary>
/// Default implementation of <see cref="ICallbackBindingNameProvider"/>
/// </summary>
public class CallbackBindingNameProvider : ICallbackBindingNameProvider
{
    public string? CallbackBindingName => HeaderPropagationMiddleware.CallbackBindingName;
}