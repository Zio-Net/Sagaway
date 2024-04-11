using Sagaway.Telemetry;
using System.Diagnostics;
using System.Collections.Concurrent;
using OpenTelemetry.Trace;

namespace Sagaway.OpenTelemetry;

public class OpenTelemetryAdapter(string activitySourceName) : ITelemetryAdapter
{
    private readonly ActivitySource _activitySource = new(activitySourceName);
    private readonly ConcurrentDictionary<string, (Activity Activity, bool IsActive)> _sagaActivities = new();
    private readonly ConcurrentDictionary<string, (Activity Activity, bool IsActive)> _operationActivities = new();

    private async Task<(ActivityTraceId, ActivitySpanId)> GetParentTraceContextIfDeactivated(SagaTelemetryContext sagaTelemetryContext)
    {
        string? traceIdString = await sagaTelemetryContext.TelemetryDataPersistence.RetrieveDataAsync($"Saga:{sagaTelemetryContext.SagaId}:TraceId");
        string? spanIdString = await sagaTelemetryContext.TelemetryDataPersistence.RetrieveDataAsync($"Saga:{sagaTelemetryContext.SagaId}:SpanId");

        if (traceIdString == null || spanIdString == null) 
            return default;

        return (ActivityTraceId.CreateFromString(traceIdString.AsSpan()), ActivitySpanId.CreateFromString(spanIdString.AsSpan()));
    }

    private async Task PersistTraceContext(SagaTelemetryContext sagaTelemetryContext, ActivityTraceId traceId, ActivitySpanId spanId)
    {
        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync($"Saga:{sagaTelemetryContext.SagaId}:TraceId", traceId.ToString());
        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync($"Saga:{sagaTelemetryContext.SagaId}:SpanId", spanId.ToString());
    }

    public async Task StartSagaAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        // Check if we are resuming a deactivated saga
        var (parentTraceId, parentSpanId) = await GetParentTraceContextIfDeactivated(sagaTelemetryContext);

        // Use parentTraceId and parentSpanId to link to the original trace if saga was previously deactivated
        var activityContext = parentTraceId != default && parentSpanId != default
            ? new ActivityContext(parentTraceId, parentSpanId, ActivityTraceFlags.Recorded)
            : default;

        // Start an activity for the saga with a specific name and type
        // This creates or represents a new tracing span
        // Start a new activity with the specified or default context
        var activity = _activitySource.StartActivity($"{sagaTelemetryContext.SagaType}-{sagaTelemetryContext.SagaId}",
            ActivityKind.Server, activityContext);

        if (activity == null)
        {
            // If no activity is started, it could be due to sampling or configuration
            // In this case, there's nothing more to do
            return;
        }

        // Set initial tags for the activity, useful for querying and filtering in tracing systems
        activity.SetTag("saga.id", sagaTelemetryContext.SagaId);
        activity.SetTag("saga.type", sagaTelemetryContext.SagaType);

        _sagaActivities.TryAdd(sagaTelemetryContext.SagaId, (activity, true));
    }


    public async Task EndSagaAsync(SagaTelemetryContext sagaTelemetryContext, SagaOutcome outcome)
    {
        if (_sagaActivities.TryRemove(sagaTelemetryContext.SagaId, out var sagaEntry) && sagaEntry.IsActive)
        {
            sagaEntry.Activity.SetTag("saga.outcome", outcome.ToString());
            sagaEntry.Activity.Stop();

            // delete persisted saga trace context if no longer needed
            await sagaTelemetryContext.TelemetryDataPersistence.DeleteDataAsync($"Saga:{sagaTelemetryContext.SagaId}:TraceId");
            await sagaTelemetryContext.TelemetryDataPersistence.DeleteDataAsync($"Saga:{sagaTelemetryContext.SagaId}:SpanId");
        }
    }

    public async Task EndOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, OperationOutcome outcome)
    {
        var operationKey = $"{sagaTelemetryContext.SagaId}-{operationName}";
        if (_operationActivities.TryRemove(operationKey, out var operationEntry) && operationEntry.IsActive)
        {
            operationEntry.Activity.SetTag("operation.outcome", outcome.ToString());
            operationEntry.Activity.Stop();
        }

        await Task.CompletedTask; // Placeholder for async method structure
    }


    public async Task StartOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName)
    {
        Activity? parentActivity = _sagaActivities.TryGetValue(sagaTelemetryContext.SagaId, out var sagaEntry) && sagaEntry.IsActive ? sagaEntry.Activity : null;

        // Start or link operation activity
        var operationActivity = _activitySource.StartActivity($"Operation-{operationName}", ActivityKind.Internal, parentActivity?.Context ?? default);

        if (operationActivity == null)
        {
            return;
        }

        // Set initial tags or details for the activity
        operationActivity.SetTag("operation.name", operationName);
        operationActivity.SetTag("saga.id", sagaTelemetryContext.SagaId);

        await Task.CompletedTask;
    }

    public async Task RecordRetryAttemptAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, int attemptNumber)
    {
        var operationKey = $"{sagaTelemetryContext.SagaId}-{operationName}";
        if (_operationActivities.TryGetValue(operationKey, out var operationEntry) && operationEntry.IsActive)
        {
            operationEntry.Activity.AddEvent(new ActivityEvent($"RetryAttempt-{attemptNumber}"));
        }

        await Task.CompletedTask;
    }

    public async Task RecordCustomEventAsync(SagaTelemetryContext sagaTelemetryContext, string eventName, IDictionary<string, object>? properties = null)
    {
        if (_sagaActivities.TryGetValue(sagaTelemetryContext.SagaId, out var sagaEntry) && sagaEntry.IsActive)
        {
            var tagsCollection = new ActivityTagsCollection(properties);
            sagaEntry.Activity.AddEvent(new ActivityEvent(eventName, tags: tagsCollection));
        }

        await Task.CompletedTask;
    }

    public async Task RecordExceptionAsync(SagaTelemetryContext sagaTelemetryContext, Exception exception, string? context = null)
    {
        if (_sagaActivities.TryGetValue(sagaTelemetryContext.SagaId, out var sagaEntry) && sagaEntry.IsActive)
        {
            sagaEntry.Activity.RecordException(exception);
            if (context != null)
            {
                sagaEntry.Activity.SetTag("exception.context", context);
            }
        }

        await Task.CompletedTask;
    }

    public async Task ActivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        //The saga started a long-running operation, we need to close all open spans (the operations and the saga)
        //And mark them as pending. When the Saga Deactivate Long Operation, we can recreate linked spans for the Saga 
        //And the operations
        if (_sagaActivities.TryRemove(sagaTelemetryContext.SagaId, out var entry) && entry.IsActive)
        {
            // Mark the saga as deactivated and end the current span
            entry.Activity.Stop();
            await PersistTraceContext(sagaTelemetryContext, entry.Activity.Context.TraceId, entry.Activity.Context.SpanId);
        }

        var operationsData = string.Join(',', _operationActivities.Select(kvp =>
        {
            var (operationActivity, isActive) = kvp.Value;
            
            if (!isActive) 
                return string.Empty;
            
            operationActivity.Stop();
            
            return $"{kvp.Key}:{operationActivity.Context.TraceId}:{operationActivity.Context.SpanId}";
        }));

        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync($"Saga:{sagaTelemetryContext.SagaId}:Operations", operationsData);
    }

    public async Task DeactivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        var persistedOperationsData =
            await sagaTelemetryContext.TelemetryDataPersistence.RetrieveDataAsync(
                $"Saga:{sagaTelemetryContext.SagaId}:Operations");
        if (string.IsNullOrWhiteSpace(persistedOperationsData))
        {
            return;
        }
        //else

        var operationPersistDataArray = persistedOperationsData.Split(',');
        foreach (var operationPersistData in operationPersistDataArray)
        {
            var parts = operationPersistData.Split(':');

            if (parts.Length != 3)
                continue;

            var opKey = parts[0];
            var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
            var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());

            // Create a linked span for this operation
            var operationActivity = _activitySource.StartActivity($"ReactivatedOperation-{opKey}",
                ActivityKind.Internal, new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded));
            if (operationActivity != null)
            {
                // Restore operation activity in your tracking structure if needed
                _operationActivities[opKey] = (operationActivity, true);
                // Note: You might want to set additional tags or details on the activity based on your needs
            }
        }
    }
}