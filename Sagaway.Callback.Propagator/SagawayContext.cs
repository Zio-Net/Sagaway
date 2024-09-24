namespace Sagaway.Callback.Propagator;

public record SagawayContext(
    string? ActorId = null,
    string? ActorType = null,
    string? CallbackBindingName = null,
    string? CallbackMethodName = null,
    string? MessageDispatchTime = null,
    string? Metadata = null);