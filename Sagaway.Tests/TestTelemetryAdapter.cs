using System.Collections.Concurrent;
using System.Text;
using Sagaway.Telemetry;

namespace Sagaway.Tests;

internal class TestTelemetryAdapter : ITelemetryAdapter
{
    private int _eventCounter;
    private ConcurrentBag<TelemetryEvent> TelemetryEvents { get; } = new();

    // Atomically increments the counter and returns the new value.
    private int NextCounter() => Interlocked.Increment(ref _eventCounter);


    public Task StartSagaAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(),
            "StartSaga", $"SagaID: {sagaTelemetryContext.SagaId}, Type: {sagaTelemetryContext.SagaType}"));
        return Task.CompletedTask;
    }

    public Task EndSagaAsync(SagaTelemetryContext sagaTelemetryContext, SagaOutcome outcome)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(),
            "EndSaga", $"SagaID: {sagaTelemetryContext.SagaId}, Outcome: {outcome}"));
        return Task.CompletedTask;
    }

    public Task StartOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), 
            "StartOperation", $"SagaID: {sagaTelemetryContext.SagaId}, Operation: {operationName}"));
        return Task.CompletedTask;
    }

    public Task EndOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, OperationOutcome outcome)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(),
            "EndOperation", $"SagaID: {sagaTelemetryContext.SagaId}, Operation: {operationName}, Outcome: {outcome}"));
        return Task.CompletedTask;
    }

    public Task RecordRetryAttemptAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, int attemptNumber)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(),
            "RetryAttempt", $"SagaID: {sagaTelemetryContext.SagaId}, Operation: {operationName}, Attempt: {attemptNumber}"));
        return Task.CompletedTask;
    }

    public Task RecordCustomEventAsync(SagaTelemetryContext sagaTelemetryContext, string eventName, IDictionary<string, object>? properties = null)
    {
        var props = properties != null ? string.Join(", ", properties.Select(p => $"{p.Key}: {p.Value}")) : "None";
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(),
            "CustomEvent", $"SagaID: {sagaTelemetryContext.SagaId}, Event: {eventName}, Properties: {props}"));
        return Task.CompletedTask;
    }

    public Task RecordExceptionAsync(SagaTelemetryContext sagaTelemetryContext, Exception exception, string? context = null)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(),
            "Exception", $"SagaID: {sagaTelemetryContext.SagaId}, Context: {context}, Exception: {exception.Message}"));
        return Task.CompletedTask;
    }

    public Task ActivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(),
            "ActivateLongOperation", $"SagaID: {sagaTelemetryContext.SagaId}"));
        return Task.CompletedTask;
    }

    public Task DeactivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(),
            "DeactivateLongOperation", $"SagaID: {sagaTelemetryContext.SagaId}"));
        return Task.CompletedTask;
    }

    public string GenerateSagaTraceResult()
    {
        var sb = new StringBuilder();

        // Order events by their counter to maintain chronological order
        foreach (var telemetryEvent in TelemetryEvents.OrderBy(e => e.Counter))
        {
            sb.AppendLine($"{telemetryEvent.Counter}: {telemetryEvent.Type} - {telemetryEvent.Details}");
        }

        return sb.ToString();
    }
}