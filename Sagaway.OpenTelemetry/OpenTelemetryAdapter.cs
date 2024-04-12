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

    private readonly ConcurrentDictionary<string, (string TraceId, string SpanId)> _deactivatedOperationActivities =
        new();

    private async Task<(ActivityTraceId, ActivitySpanId)> GetParentTraceContextIfDeactivated(
        SagaTelemetryContext sagaTelemetryContext)
    {
        string? traceIdString =
            await sagaTelemetryContext.TelemetryDataPersistence.RetrieveDataAsync(
                $"Saga:{sagaTelemetryContext.SagaId}:TraceId");
        string? spanIdString =
            await sagaTelemetryContext.TelemetryDataPersistence.RetrieveDataAsync(
                $"Saga:{sagaTelemetryContext.SagaId}:SpanId");

        if (traceIdString == null || spanIdString == null)
            return default;

        return (ActivityTraceId.CreateFromString(traceIdString.AsSpan()),
            ActivitySpanId.CreateFromString(spanIdString.AsSpan()));
    }

    private async Task PersistSagaTraceContext(SagaTelemetryContext sagaTelemetryContext, ActivityTraceId traceId,
        ActivitySpanId spanId)
    {
        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync(
            $"Saga:{sagaTelemetryContext.SagaId}:TraceId", traceId.ToString());
        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync($"Saga:{sagaTelemetryContext.SagaId}:SpanId",
            spanId.ToString());
    }

    private async Task PersistActiveOperationStateAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        var operationsData = string.Join(',', _operationActivities.
            Where(op=>op.Value.IsActive).Select(kvp =>
        {
            var (operationActivity, _) = kvp.Value;


            return $"{kvp.Key}:{operationActivity.Context.TraceId}:{operationActivity.Context.SpanId}";
        }));

        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync(
            $"Saga:{sagaTelemetryContext.SagaId}:Operations", operationsData);
    }

    private async Task ReadDeactivatedOperations(SagaTelemetryContext sagaTelemetryContext)
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
            var traceId = parts[1];
            var spanId = parts[2];

            _deactivatedOperationActivities[opKey] = (traceId, spanId);
        }
    }

    public async Task StartSagaAsync(SagaTelemetryContext sagaTelemetryContext, bool isNew)
    {
        ActivityContext activityContext = default;
        if (!isNew)
        {
            // Check if we are resuming a deactivated saga
            var (parentTraceId, parentSpanId) = await GetParentTraceContextIfDeactivated(sagaTelemetryContext);

            // Use parentTraceId and parentSpanId to link to the original trace if saga was previously deactivated
            activityContext = parentTraceId != default && parentSpanId != default
                ? new ActivityContext(parentTraceId, parentSpanId, ActivityTraceFlags.Recorded)
                : default;

            await ReadDeactivatedOperations(sagaTelemetryContext);
        }


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
            await sagaTelemetryContext.TelemetryDataPersistence.DeleteDataAsync(
                $"Saga:{sagaTelemetryContext.SagaId}:TraceId");
            await sagaTelemetryContext.TelemetryDataPersistence.DeleteDataAsync(
                $"Saga:{sagaTelemetryContext.SagaId}:SpanId");
        }
    }

    public async Task StartOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName)
    {
        Activity? operationActivity;

        //check if the operation has been deactivated
        if (_deactivatedOperationActivities.TryGetValue($"{sagaTelemetryContext.SagaId}-{operationName}",
                out var operationEntry))
        {
            // Create a linked span for this operation
            operationActivity = _activitySource.StartActivity($"ReactivatedOperation-{operationName}",
                ActivityKind.Internal,
                new ActivityContext(ActivityTraceId.CreateFromString(operationEntry.TraceId.AsSpan()),
                    ActivitySpanId.CreateFromString(operationEntry.SpanId.AsSpan()), ActivityTraceFlags.Recorded));

            if (operationActivity != null)
            {
                // Restore operation activity in your tracking structure if needed
                _operationActivities[$"{sagaTelemetryContext.SagaId}-{operationName}"] = (operationActivity, true);
            }
        }
        else
        {
            Activity? parentActivity =
                _sagaActivities.TryGetValue(sagaTelemetryContext.SagaId, out var sagaEntry) && sagaEntry.IsActive
                    ? sagaEntry.Activity
                    : null;

            // Start or link operation activity
            operationActivity = _activitySource.StartActivity($"Operation-{operationName}", ActivityKind.Internal,
                parentActivity?.Context ?? default);
        }

        if (operationActivity == null)
        {
            return;
        }

        // Set initial tags or details for the activity
        operationActivity.SetTag("operation.name", operationName);
        operationActivity.SetTag("saga.id", sagaTelemetryContext.SagaId);

        //add the activity to the tracking structure
        _operationActivities[$"{sagaTelemetryContext.SagaId}-{operationName}"] = (operationActivity, true);

        await PersistActiveOperationStateAsync(sagaTelemetryContext);
    }

    public async Task EndOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName,
        OperationOutcome outcome)
    {
        var operationKey = $"{sagaTelemetryContext.SagaId}-{operationName}";
        if (_operationActivities.TryRemove(operationKey, out var operationEntry) && operationEntry.IsActive)
        {
            operationEntry.Activity.SetTag("operation.outcome", outcome.ToString());
            operationEntry.Activity.Stop();
        }

        await PersistActiveOperationStateAsync(sagaTelemetryContext);
    }

    public async Task RecordRetryAttemptAsync(SagaTelemetryContext sagaTelemetryContext, string operationName,
        int attemptNumber)
    {
        var operationKey = $"{sagaTelemetryContext.SagaId}-{operationName}";
        if (_operationActivities.TryGetValue(operationKey, out var operationEntry) && operationEntry.IsActive)
        {
            operationEntry.Activity.AddEvent(new ActivityEvent($"RetryAttempt-{attemptNumber}"));
        }

        await Task.CompletedTask;
    }

    public async Task RecordCustomEventAsync(SagaTelemetryContext sagaTelemetryContext, string eventName,
        IDictionary<string, object>? properties = null)
    {
        if (_sagaActivities.TryGetValue(sagaTelemetryContext.SagaId, out var sagaEntry) && sagaEntry.IsActive)
        {
            if (properties == null)
            {
                sagaEntry.Activity.AddEvent(new ActivityEvent(eventName));
                return;
            }
            //else

            var tagsCollection = new ActivityTagsCollection(properties!);
            sagaEntry.Activity.AddEvent(new ActivityEvent(eventName, tags: tagsCollection));
        }

        await Task.CompletedTask;
    }

    public async Task RecordExceptionAsync(SagaTelemetryContext sagaTelemetryContext, Exception exception,
        string? context = null)
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
        // Check and possibly deactivate the main saga activity
        if (_sagaActivities.TryGetValue(sagaTelemetryContext.SagaId, out var sagaEntry) && sagaEntry.IsActive)
        {
            sagaEntry.Activity.Stop();
            await PersistSagaTraceContext(sagaTelemetryContext, sagaEntry.Activity.Context.TraceId,
                sagaEntry.Activity.Context.SpanId);
            _sagaActivities[sagaTelemetryContext.SagaId] = (sagaEntry.Activity, false);
        }

        // Stop all operation activities and mark them as inactive
        foreach (var (key, (activity, isActive)) in _operationActivities)
        {
            if (isActive)
            {
                activity.Stop();
                _operationActivities[key] = (activity, false);
            }
        }
    }
}


    