namespace Sagaway.Callback.Propagator;

/// <summary>
/// Default implementation of <see cref="ICallbackQueueNameProvider"/>
/// </summary>
public class CallbackQueueNameProvider : ICallbackQueueNameProvider
{
    public string CallbackQueueName => HeaderPropagationMiddleware.CallbackQueueName.Value ?? throw new InvalidOperationException("CallbackQueueName is not set");
}