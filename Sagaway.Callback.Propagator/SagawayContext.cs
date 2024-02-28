namespace Sagaway.Callback.Propagator;

public record SagawayContext(string? ActorId, string? ActorType, string? CallbackQueueName,
    string? CallbackMethodName, string? MessageDispatchTime);