namespace Sagaway.Callback.Propagator;

/// <summary>
/// Provide context information for the current call.
/// </summary>
public interface ISagawayContextInformationProvider : ICallbackBindingNameProvider
{
    /// <summary>
    /// Gets the message dispatch time.
    /// </summary>
    DateTimeOffset MessageDispatchTime { get; }

    /// <summary>
    /// Gets the custom metadata of type <typeparamref name="TMetadata"/>.
    /// </summary>
    /// <typeparam name="TMetadata">The type of the custom metadata.</typeparam>
    /// <returns>The custom metadata of type <typeparamref name="TMetadata"/>.</returns>
    TMetadata GetCustomMetadata<TMetadata>();
}
