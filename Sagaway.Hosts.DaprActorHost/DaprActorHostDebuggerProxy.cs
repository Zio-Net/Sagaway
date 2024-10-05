namespace Sagaway.Hosts.DaprActorHost;

/// <summary>
/// A debugger proxy for the <see cref="DaprActorHost{TEOperations}"/> class.
/// This class provides a simplified view of the saga state for debugging purposes,
/// particularly focusing on the saga status formatted with new lines for readability in the debugger.
/// </summary>
/// <typeparam name="TEOperations">The enum representing the operations of the saga.</typeparam>
internal class DaprActorHostDebuggerProxy<TEOperations>(DaprActorHost<TEOperations> actorHost)
    where TEOperations : Enum
{
    /// <summary>
    /// Gets the current saga status with formatted new lines for better readability in the debugger.
    /// </summary>
    public string SagaStatus => actorHost.GetSagaStatus().Replace("\n", Environment.NewLine);
}