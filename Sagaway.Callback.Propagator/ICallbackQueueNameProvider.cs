namespace Sagaway.Callback.Propagator;

/// <summary>
/// Provide the callback queue name as received from the request headers
/// </summary>
public interface ICallbackQueueNameProvider
{
    string CallbackQueueName { get; }
}