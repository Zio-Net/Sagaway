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

    public void StartSaga(string sagaId, string sagaType)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "StartSaga", $"SagaID: {sagaId}, Type: {sagaType}"));
    }

    public void EndSaga(string sagaId, SagaOutcome outcome)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "EndSaga", $"SagaID: {sagaId}, Outcome: {outcome}"));
    }

    public void StartOperation(string sagaId, string operationName)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "StartOperation", $"SagaID: {sagaId}, Operation: {operationName}"));
    }

    public void EndOperation(string sagaId, string operationName, OperationOutcome outcome)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "EndOperation", $"SagaID: {sagaId}, Operation: {operationName}, Outcome: {outcome}"));
    }

    public void RecordRetryAttempt(string sagaId, string operationName, int attemptNumber)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "RetryAttempt", $"SagaID: {sagaId}, Operation: {operationName}, Attempt: {attemptNumber}"));
    }

    public void RecordCustomEvent(string sagaId, string eventName, IDictionary<string, object>? properties = null)
    {
        var props = properties != null ? string.Join(", ", properties.Select(p => $"{p.Key}: {p.Value}")) : "None";
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "CustomEvent", $"SagaID: {sagaId}, Event: {eventName}, Properties: {props}"));
    }

    public void RecordException(string sagaId, Exception exception, string? context = null)
    {
        TelemetryEvents.Add(new TelemetryEvent(NextCounter(), "Exception", $"SagaID: {sagaId}, Context: {context}, Exception: {exception.Message}"));
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