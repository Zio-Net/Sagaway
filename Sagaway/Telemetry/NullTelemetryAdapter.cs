namespace Sagaway.Telemetry;

/// <summary>
/// A no-operation implementation of <see cref="ITelemetryAdapter"/>.
/// Used as a default to aTask null checks, following the Null Object Design Pattern.
/// </summary>
public class NullTelemetryAdapter : ITelemetryAdapter
{
    public void Initialize(ITelemetryDataPersistence dataPersistence)
    {
        // No operation
    }

    public Task StartSagaAsync(string sagaId, string sagaType)
    {
        return Task.CompletedTask;
    }

    public Task EndSagaAsync(string sagaId, SagaOutcome outcome)
    {
        return Task.CompletedTask;
    }

    public Task StartOperationAsync(string sagaId, string operationName)
    {
        return Task.CompletedTask;
    }

    public Task EndOperationAsync(string sagaId, string operationName, OperationOutcome outcome)
    {
        return Task.CompletedTask;
    }

    public Task RecordRetryAttemptAsync(string sagaId, string operationName, int attemptNumber)
    {
        return Task.CompletedTask;
    }

    public Task RecordCustomEventAsync(string sagaId, string eventName, IDictionary<string, object>? properties = null)
    {
        return Task.CompletedTask;
    }

    public Task RecordExceptionAsync(string sagaId, Exception exception, string? context = null)
    {
        return Task.CompletedTask;
    }

    public Task ActivateLongOperationAsync(string sagaId)
    {
        return Task.CompletedTask;
    }

    public Task DeactivateLongOperationAsync(string sagaId)
    {
        return Task.CompletedTask;
    }
}