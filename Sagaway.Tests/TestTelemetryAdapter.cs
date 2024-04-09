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

    public void Initialize(ITelemetryDataPersistence dataPersistence)
    {
        // No operation
    }

    public Task StartSagaAsync(string sagaId, string sagaType)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "StartSaga", $"SagaID: {sagaId}, Type: {sagaType}"));
        return Task.CompletedTask;
    }

    public Task EndSagaAsync(string sagaId, SagaOutcome outcome)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "EndSaga", $"SagaID: {sagaId}, Outcome: {outcome}"));
        return Task.CompletedTask;
    }

    public Task StartOperationAsync(string sagaId, string operationName)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "StartOperation", $"SagaID: {sagaId}, Operation: {operationName}"));
        return Task.CompletedTask;
    }

    public Task EndOperationAsync(string sagaId, string operationName, OperationOutcome outcome)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "EndOperation", $"SagaID: {sagaId}, Operation: {operationName}, Outcome: {outcome}"));
        return Task.CompletedTask;
    }

    public Task RecordRetryAttemptAsync(string sagaId, string operationName, int attemptNumber)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "RetryAttempt", $"SagaID: {sagaId}, Operation: {operationName}, Attempt: {attemptNumber}"));
        return Task.CompletedTask;
    }

    public Task RecordCustomEventAsync(string sagaId, string eventName, IDictionary<string, object>? properties = null)
    {
        var props = properties != null ? string.Join(", ", properties.Select(p => $"{p.Key}: {p.Value}")) : "None";
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "CustomEvent", $"SagaID: {sagaId}, Event: {eventName}, Properties: {props}"));
        return Task.CompletedTask;
    }

    public Task RecordExceptionAsync(string sagaId, Exception exception, string? context = null)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "Exception", $"SagaID: {sagaId}, Context: {context}, Exception: {exception.Message}"));
        return Task.CompletedTask;
    }

    public Task ActivateLongOperationAsync(string sagaId)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "ActivateLongOperation", $"SagaID: {sagaId}"));
        return Task.CompletedTask;
    }

    public Task DeactivateLongOperationAsync(string sagaId)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "DeactivateLongOperation", $"SagaID: {sagaId}"));
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