namespace Sagaway.Telemetry;

/// <summary>
/// A no-operation implementation of <see cref="ITelemetryAdapter"/>.
/// Used as a default to avoid null checks, following the Null Object Design Pattern.
/// </summary>
public class NullTelemetryAdapter : ITelemetryAdapter
{
    public void StartSaga(string sagaId, string sagaType)
    {
        // No operation
    }

    public void EndSaga(string sagaId, SagaOutcome outcome)
    {
        // No operation
    }

    public void StartOperation(string sagaId, string operationName)
    {
        // No operation
    }

    public void EndOperation(string sagaId, string operationName, OperationOutcome outcome)
    {
        // No operation
    }

    public void RecordRetryAttempt(string sagaId, string operationName, int attemptNumber)
    {
        // No operation
    }

    public void RecordCustomEvent(string sagaId, string eventName, IDictionary<string, object>? properties = null)
    {
        // No operation
    }

    public void RecordException(string sagaId, Exception exception, string? context = null)
    {
        // No operation
    }
}