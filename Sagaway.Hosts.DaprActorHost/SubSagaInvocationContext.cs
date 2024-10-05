using System.Text.Json;

namespace Sagaway.Hosts.DaprActorHost;

/// <summary>
/// Represents the context for invoking a sub-saga in the Sagaway framework.
/// This context includes the method to be invoked and the serialized parameters in JSON format.
/// </summary>
public record SubSagaInvocationContext
{
    /// <summary>
    /// Gets or sets the name of the method to be invoked on the sub-saga actor.
    /// </summary>
    public string MethodName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON-serialized string representing the parameters to be passed to the method.
    /// </summary>
    public string ParametersJson { get; init; } = string.Empty;
}