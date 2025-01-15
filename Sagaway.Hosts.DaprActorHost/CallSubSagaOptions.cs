namespace Sagaway.Hosts.DaprActorHost;

/// <summary>
/// Represents the options for configuring a sub-saga invocation call.
/// </summary>
public record CallSubSagaOptions
{
    /// <summary>
    /// Gets the name of the callback method in the main saga that should be invoked
    /// once the sub-saga completes its operation.
    /// </summary>
    public string CallbackMethodName { get; init; } = string.Empty;

    /// <summary>
    /// Gets any additional metadata specific to the sub-saga invocation,
    /// which will be included in the Dapr binding call context.
    /// </summary>
    public string CustomSagawayMetadata { get; init; } = string.Empty;

    /// <summary>
    /// Gets the custom binding name to use for the Dapr binding operation.
    /// If not specified, the default binding name is used.
    /// </summary>
    public string UseBindingName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a dictionary of additional metadata to include in the binding context.
    /// These values will override any defaults if the same keys are present.
    /// </summary>
    public Dictionary<string, string>? CustomBindingMetadata { get; init; }
}