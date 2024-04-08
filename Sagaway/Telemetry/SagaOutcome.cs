namespace Sagaway.Telemetry;

/// <summary>
/// Enum to represent the possible outcomes of the Saga as a whole.
/// </summary>
public enum SagaOutcome
{
    Succeeded,
    PartiallyReverted,
    Reverted
}