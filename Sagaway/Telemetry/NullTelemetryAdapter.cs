namespace Sagaway.Telemetry;

/// <summary>
/// A no-operation implementation of <see cref="ITelemetryAdapter"/>.
/// Used as a default to aTask null checks, following the Null Object Design Pattern.
/// </summary>
public class NullTelemetryAdapter : ITelemetryAdapter
{
    public Task StartSagaAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        return Task.CompletedTask;
    }

    public Task EndSagaAsync(SagaTelemetryContext sagaTelemetryContext, SagaOutcome outcome)
    {
        return Task.CompletedTask;
    }

    public Task StartOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName)
    {
        return Task.CompletedTask;
    }

    public Task EndOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, OperationOutcome outcome)
    {
        return Task.CompletedTask;
    }

    public Task RecordRetryAttemptAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, int attemptNumber)
    {
        return Task.CompletedTask;
    }

    public Task RecordCustomEventAsync(SagaTelemetryContext sagaTelemetryContext, string eventName, IDictionary<string, object>? properties = null)
    {
        return Task.CompletedTask;
    }

    public Task RecordExceptionAsync(SagaTelemetryContext sagaTelemetryContext, Exception exception, string? context = null)
    {
        return Task.CompletedTask;
    }

    public Task ActivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        return Task.CompletedTask;
    }
}