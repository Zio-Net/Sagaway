namespace Sagaway.Hosts.DaprActorHost;

public record SubSagaInvocationContext
{
  public required string MethodName { get; init; }
  public required IDictionary<string, string> CallbackContext { get; init; }
  public required string ParametersJson { get; init; }
}