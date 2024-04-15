using Sagaway.Telemetry;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace Sagaway.OpenTelemetry;

public class OpenTelemetryAdapter(string activitySourceName) : ITelemetryAdapter
{
    private readonly ActivitySource _activitySource = new(activitySourceName);
    private readonly ConcurrentDictionary<string, Activity> _sagaActivities = new();
    private readonly ConcurrentDictionary<string, Activity> _operationActivities = new();

    private readonly ConcurrentDictionary<string, (string TraceId, string SpanId)> _deactivatedOperationActivities =
        new();

    private async Task<(ActivityTraceId, ActivitySpanId)> GetParentTraceContextIfDeactivated(
        SagaTelemetryContext stc)
    {
        string? traceIdString = await stc.TelemetryDataPersistence.RetrieveDataAsync(
                $"Saga:{stc.SagaId}:TraceId");
        string? spanIdString = await stc.TelemetryDataPersistence.RetrieveDataAsync(
                $"Saga:{stc.SagaId}:SpanId");

        if (traceIdString == null || spanIdString == null)
            return default;

        stc.Logger.LogInformation("Reactivating saga {SagaId} with traceId {TraceId} and spanId {SpanId}",
                       stc.SagaId, traceIdString, spanIdString);

        return (ActivityTraceId.CreateFromString(traceIdString.AsSpan()),
            ActivitySpanId.CreateFromString(spanIdString.AsSpan()));
    }

    private async Task PersistSagaTraceContext(SagaTelemetryContext stc, ActivityTraceId traceId,
        ActivitySpanId spanId)
    {
        await stc.TelemetryDataPersistence.StoreDataAsync(
            $"Saga:{stc.SagaId}:TraceId", traceId.ToString());
        await stc.TelemetryDataPersistence.StoreDataAsync($"Saga:{stc.SagaId}:SpanId",
            spanId.ToString());

        stc.Logger.LogInformation("Persisted trace context for saga {SagaId} with traceId {TraceId} and spanId {SpanId}",
                       stc.SagaId, traceId, spanId);
    }

    private async Task PersistActiveOperationStateAsync(SagaTelemetryContext stc)
    {
        var operationsData = string.Join(',', _operationActivities.Select(kvp =>
        {
            var operationActivity = kvp.Value;
            return $"{kvp.Key}:{operationActivity.Context.TraceId}:{operationActivity.Context.SpanId}";
        }));

        await stc.TelemetryDataPersistence.StoreDataAsync(
            $"Saga:{stc.SagaId}:Operations", operationsData);

        stc.Logger.LogInformation("Persisted active operations for saga {SagaId}", stc.SagaId);
        stc.Logger.LogDebug("Operations: {OperationsData}", operationsData);
    }

    private async Task ReadDeactivatedOperations(SagaTelemetryContext stc)
    {
        var persistedOperationsData =
            await stc.TelemetryDataPersistence.RetrieveDataAsync(
                $"Saga:{stc.SagaId}:Operations");
        
        if (string.IsNullOrWhiteSpace(persistedOperationsData))
        {
            stc.Logger.LogInformation("No deactivated operations found for saga {SagaId}", stc.SagaId);
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
            
            stc.Logger.LogDebug("Reactivating operation {OperationKey} with traceId {TraceId} and spanId {SpanId}",
                               opKey, traceId, spanId);
        }
    }

    private string GetSagaActivityName(SagaTelemetryContext stc) => $"{stc.SagaType}-{stc.SagaId}";

    private string GetOperationActivityName(SagaTelemetryContext stc, string operationName) =>
        $"{stc.SagaType}-{stc.SagaId}-{operationName}";

    public async Task StartSagaAsync(SagaTelemetryContext stc, bool isNew)
    {
        try
        {
            var sagaActivityName = GetSagaActivityName(stc);

            ActivityContext activityContext = default;

            if (!isNew)
            {
                // Check if we are resuming a deactivated saga
                var (parentTraceId, parentSpanId) = await GetParentTraceContextIfDeactivated(stc);

                // Use parentTraceId and parentSpanId to link to the original trace if saga was previously deactivated
                activityContext = parentTraceId != default && parentSpanId != default
                    ? new ActivityContext(parentTraceId, parentSpanId, ActivityTraceFlags.Recorded)
                    : default;

                await ReadDeactivatedOperations(stc);

                stc.Logger.LogInformation("Reactivated saga {SagaId} with traceId {TraceId} and spanId {SpanId}",
                    stc.SagaId, parentTraceId, parentSpanId);
            }
            else
            {
                // Extract the context from the incoming HTTP request's headers
                if (Activity.Current != null)
                {
                    // Use the current activity (which is automatically created for incoming requests by ASP.NET Core)
                    activityContext = Activity.Current.Context;
                }
            }

            // Start an activity for the saga with a specific name and type
            // This creates or represents a new tracing span
            // Start a new activity with the specified or default context
            var activity =
                _activitySource.StartActivity(sagaActivityName, ActivityKind.Server, activityContext);

            if (activity == null)
            {
                // If no activity is started, it could be due to sampling or configuration
                // In this case, there's nothing more to do
                stc.Logger.LogWarning("Failed to start activity for saga {SagaId}", stc.SagaId);
                return;
            }

            // Set initial tags for the activity, useful for querying and filtering in tracing systems
            activity.SetTag("saga.id", stc.SagaId);
            activity.SetTag("saga.type", stc.SagaType);

            _sagaActivities[sagaActivityName] = activity;

            stc.Logger.LogInformation("Started activity for saga {SagaId}", stc.SagaId);
        }
        catch (Exception ex)
        {
            stc.Logger.LogError(ex, "Failed to start activity for saga {SagaId}", stc.SagaId);
        }
    }


    public async Task EndSagaAsync(SagaTelemetryContext stc, SagaOutcome outcome)
    {
        try
        {
            if (_sagaActivities.TryRemove(GetSagaActivityName(stc), out var sagaActivity))
            {
                sagaActivity.SetTag("saga.outcome", outcome.ToString());
                sagaActivity.Stop();

                // delete persisted saga trace context if no longer needed
                await stc.TelemetryDataPersistence.DeleteDataAsync(
                    $"Saga:{stc.SagaId}:TraceId");
                await stc.TelemetryDataPersistence.DeleteDataAsync(
                    $"Saga:{stc.SagaId}:SpanId");

                stc.Logger.LogInformation("Ended activity for saga {SagaId} with outcome {Outcome}",
                    stc.SagaId, outcome);
            }
            else
            {
                stc.Logger.LogWarning("Ending Saga Activity: No activity found for saga {SagaId}", stc.SagaId);
            }
        }
        catch (Exception ex)
        {
            stc.Logger.LogError(ex, "Failed to end activity for saga {SagaId}", stc.SagaId);
        }
    }

    public async Task StartOperationAsync(SagaTelemetryContext stc, string operationName)
    {
        try
        {
            Activity? operationActivity;
            var operationActivityName = GetOperationActivityName(stc, operationName);
            //check if the operation has been deactivated
            if (_deactivatedOperationActivities.TryGetValue(operationActivityName,
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
                    _operationActivities[operationActivityName] = operationActivity;
                    stc.Logger.LogInformation("Reactivated activity for operation {OperationName}", operationName);
                }
            }
            else //no previous activity found, use the saga activity as parent
            {
                _sagaActivities.TryGetValue(GetSagaActivityName(stc), out var parentActivity);

                //if parent activity is not found, use the current activity
                parentActivity ??= Activity.Current;

                // Start or link operation activity
                operationActivity = _activitySource.StartActivity($"Operation-{operationName}", ActivityKind.Internal,
                    parentActivity?.Context ?? default);

                stc.Logger.LogInformation("Started activity for operation {OperationName}", operationName);
            }

            if (operationActivity == null)
            {
                stc.Logger.LogWarning("Failed to start activity for operation {OperationName}", operationName);
                return;
            }

            // Set initial tags or details for the activity
            operationActivity.SetTag("operation.name", operationName);
            operationActivity.SetTag("saga.id", stc.SagaId);

            //add the activity to the tracking structure
            _operationActivities[operationActivityName] = operationActivity;

            await PersistActiveOperationStateAsync(stc);
        }
        catch (Exception ex)
        {
            stc.Logger.LogError(ex, "Failed to start activity for operation {OperationName}", operationName);
        }
    }

    public async Task EndOperationAsync(SagaTelemetryContext stc, string operationName,
        OperationOutcome outcome)
    {
        try
        {
            var operationKey = GetOperationActivityName(stc, operationName);
            if (_operationActivities.TryRemove(operationKey, out var operationActivity))
            {
                operationActivity.SetTag("operation.outcome", outcome.ToString());
                operationActivity.Stop();
                await PersistActiveOperationStateAsync(stc);

                stc.Logger.LogInformation("Ended activity for operation {OperationName} with outcome {Outcome}",
                    operationName, outcome);
            }
            else
            {
                stc.Logger.LogWarning("Ending Operation Activity: No activity found for operation {OperationName}",
                    operationName);
            }
        }
        catch (Exception ex)
        {
            stc.Logger.LogError(ex, "Failed to end activity for operation {OperationName}", operationName);
        }
    }

    public async Task RecordRetryAttemptAsync(SagaTelemetryContext stc, string operationName,
        int attemptNumber)
    {
        try
        {
            var operationKey = GetOperationActivityName(stc, operationName);
            if (_operationActivities.TryGetValue(operationKey, out var operationActivity))
            {
                operationActivity.AddEvent(new ActivityEvent($"RetryAttempt-{attemptNumber}",
                    tags: new()
                    { { "retry.attemptNumber", attemptNumber }
                    }));

                stc.Logger.LogInformation("Recorded retry attempt {AttemptNumber} for operation {OperationName}",
                    attemptNumber, operationName);
            }
            else
            {
                stc.Logger.LogWarning("Recording Retry Attempt: No activity found for operation {OperationName}",
                    operationName);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            stc.Logger.LogError(ex, "Failed to record retry attempt {AttemptNumber} for operation {OperationName}",
                               attemptNumber, operationName);
        }
    }

    public async Task RecordCustomEventAsync(SagaTelemetryContext stc, string eventName,
        IDictionary<string, object>? properties = null)
    {
        try
        {
            if (_sagaActivities.TryGetValue(GetSagaActivityName(stc), out var sagaActivity))
            {
                stc.Logger.LogInformation("Recording custom event {EventName} for saga {SagaId}", eventName,
                    stc.SagaId);

                if (properties == null)
                {
                    sagaActivity.AddEvent(new ActivityEvent(eventName));
                    return;
                }
                //else

                var tagsCollection = new ActivityTagsCollection(properties!);
                sagaActivity.AddEvent(new ActivityEvent(eventName, tags: tagsCollection));
            }
            else
            {
                stc.Logger.LogWarning("Recording Custom Event: No activity found for saga {SagaId}", stc.SagaId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            stc.Logger.LogError(ex, "Failed to record custom event {EventName} for saga {SagaId}", eventName, stc.SagaId);
        }
    }

    public async Task RecordExceptionAsync(SagaTelemetryContext stc, Exception exception,
        string? context = null)
    {
        try
        {
            if (_sagaActivities.TryGetValue(GetSagaActivityName(stc), out var sagaActivity))
            {
                sagaActivity.RecordException(exception);
                if (context != null)
                {
                    sagaActivity.SetTag("exception.context", context);
                }

                stc.Logger.LogInformation(exception, "Recorded exception for saga {SagaId}", stc.SagaId);
            }
            else
            {
                stc.Logger.LogWarning("Recording Exception: No activity found for saga {SagaId}", stc.SagaId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            stc.Logger.LogError(ex, "Failed to record exception for saga {SagaId}", stc.SagaId);
        }
    }

    public async Task ActivateLongOperationAsync(SagaTelemetryContext stc)
    {
        try
        {
            // Deactivate the main saga activity
            if (_sagaActivities.TryRemove(GetSagaActivityName(stc), out var sagaActivity))
            {
                sagaActivity.Stop();
                await PersistSagaTraceContext(stc, sagaActivity.Context.TraceId,
                    sagaActivity.Context.SpanId);
                _sagaActivities[stc.SagaId] = sagaActivity;

                stc.Logger.LogInformation("Deactivated saga activity {SagaId}", stc.SagaId);
            }
            else
            {
                stc.Logger.LogWarning("Deactivating Saga Activity: No activity found for saga {SagaId}", stc.SagaId);
            }

            // Stop all operation activities and mark them as inactive
            foreach (var (_, activity) in _operationActivities)
            {
                activity.Stop();
                stc.Logger.LogInformation("Deactivated operation activity {OperationName}", activity.DisplayName);
            }

            _operationActivities.Clear();
        }
        catch (Exception ex)
        {
            stc.Logger.LogError(ex, "Failed to deactivate saga activity {SagaId}", stc.SagaId);
        }
    }
}


    