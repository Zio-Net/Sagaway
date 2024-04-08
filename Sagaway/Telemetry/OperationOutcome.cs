namespace Sagaway.Telemetry;

/// <summary>
/// Enum to represent the possible outcomes of individual operations within the Saga.
/// </summary>
public enum OperationOutcome
{
    Succeeded,
    Failed,
    RevertFailed,
    Reverted
}