namespace Sagaway.Callback.Context;

/// <summary>
/// Provides the caller ID for Sagaway services.
/// <remarks>
/// We use this interface to distinguish each service type from each other.
/// Since multiple instances of the service can accept the call all of them need to share the same callerId we must find a unique id which is shared among the service instances.
/// The default implementation takes the service host assembly name and version as the callerId.
/// The Sagaway routing framework uses this caller id to check if the current context belongs to the current service
/// and to ensure that it removes this context layer on calling back.
/// If you have multiple services that shares the same assembly name and version, implement your own CallerId and use a custom identifier for the callerId service type.
/// </remarks>
/// </summary>
public interface ISagawayCallerIdProvider
{
    /// <summary>
    /// Gets or sets the caller ID.
    /// </summary>
    string CallerId { get; set; }
}

