namespace Sagaway.Callback.Propagator;

/// <summary>
/// Defines the contract for managing the Sagaway context, providing methods
/// to retrieve and apply the context across distributed services.
/// </summary>
public interface ISagawayContextManager
{
    /// <summary>
    /// Gets the current Sagaway context as a base64 serialized string. 
    /// This context can be used to propagate across service boundaries.
    /// </summary>
    string Context { get; }

    /// <summary>
    /// Gets the current Sagaway context with the custom metadata property contains the custom metadata
    /// as a base64 serialized string.
    /// This context can be used to propagate across service boundaries.
    /// </summary>
    string GetContextWithMetadata(string customMetadata = "");

    /// <summary>
    /// Applies a given Sagaway context to the current execution.
    /// This method is used to restore the context in downstream services.
    /// </summary>
    /// <param name="sagaContext">The serialized context string to be applied.</param>
    void ApplyContext(string sagaContext);
}

