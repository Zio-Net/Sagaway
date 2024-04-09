using Sagaway.Telemetry;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Sagaway.OpenTelemetry;

public class OpenTelemetryAdapter(string activitySourceName) : ITelemetryAdapter
{
    private readonly ActivitySource _activitySource = new(activitySourceName);
    private ITelemetryDataPersistence? _dataPersistence;

    public void Initialize(ITelemetryDataPersistence dataPersistence)
    {
        _dataPersistence = dataPersistence;
    }

    public async Task StartSagaAsync(string sagaId, string sagaType)
    {
        // Start an activity for the saga with a specific name and type
        // This creates or represents a new tracing span
        var activity = _activitySource.StartActivity($"{sagaType}-{sagaId}", ActivityKind.Server);
        if (activity == null)
        {
            // If no activity is started, it could be due to sampling or configuration
            // In this case, there's nothing more to do
            return;
        }

        // Set initial tags for the activity, useful for querying and filtering in tracing systems
        activity.SetTag("saga.id", sagaId);
        activity.SetTag("saga.type", sagaType);

        // Check if data persistence is configured to store trace IDs for later use
        if (_dataPersistence != null)
        {
            // Persist the TraceId and SpanId associated with this saga
            // These IDs can be used to link other spans or to retrieve the saga's trace later
            await _dataPersistence.StoreDataAsync($"Saga:{sagaId}:TraceId", activity.Context.TraceId.ToString());
            await _dataPersistence.StoreDataAsync($"Saga:{sagaId}:SpanId", activity.Context.SpanId.ToString());
        }
    }


    public Task EndSagaAsync(string sagaId, SagaOutcome outcome)
    {
        throw new NotImplementedException();
    }

    public Task StartOperationAsync(string sagaId, string operationName)
    {
        throw new NotImplementedException();
    }

    public Task EndOperationAsync(string sagaId, string operationName, OperationOutcome outcome)
    {
        throw new NotImplementedException();
    }

    public Task RecordRetryAttemptAsync(string sagaId, string operationName, int attemptNumber)
    {
        throw new NotImplementedException();
    }

    public Task RecordCustomEventAsync(string sagaId, string eventName, IDictionary<string, object>? properties = null)
    {
        throw new NotImplementedException();
    }

    public Task RecordExceptionAsync(string sagaId, Exception exception, string? context = null)
    {
        throw new NotImplementedException();
    }

    public Task ActivateLongOperationAsync(string sagaId)
    {
        throw new NotImplementedException();
    }

    public Task DeactivateLongOperationAsync(string sagaId)
    {
        throw new NotImplementedException();
    }
}