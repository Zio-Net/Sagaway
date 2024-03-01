namespace Sagaway.Callback.Propagator;

public record SagawayContext(string? ActorId, string? ActorType, string? CallbackBindingName,
    string? CallbackMethodName, string? MessageDispatchTime);