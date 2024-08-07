namespace Sagaway.Hosts.DaprActorHost;

internal class DaprActorHostDebuggerProxy<TEOperations>(DaprActorHost<TEOperations> actorHost)
    where TEOperations : Enum
{
    public string SagaStatus => actorHost.GetSagaStatus().Replace("\n", Environment.NewLine);
}