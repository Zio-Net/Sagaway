using Sagaway.Telemetry;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Sagaway.OpenTelemetry;

public class OpenTelemetryAdapter(string activitySourceName) : ITelemetryAdapter
{
    private readonly ActivitySource _activitySource = new(activitySourceName);

    public async Task StartSagaAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        // Start an activity for the saga with a specific name and type
        // This creates or represents a new tracing span
        var activity = _activitySource.StartActivity($"{sagaTelemetryContext.SagaType}-{sagaTelemetryContext.SagaId}",
            ActivityKind.Server);

        if (activity == null)
        {
            // If no activity is started, it could be due to sampling or configuration
            // In this case, there's nothing more to do
            return;
        }

        // Set initial tags for the activity, useful for querying and filtering in tracing systems
        activity.SetTag("saga.id", sagaTelemetryContext.SagaId);
        activity.SetTag("saga.type", sagaTelemetryContext.SagaType);

        // Persist the TraceId and SpanId associated with this saga
        // These IDs can be used to link other spans or to retrieve the saga's trace later
        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync(
            $"Saga:{sagaTelemetryContext.SagaId}:TraceId", activity.Context.TraceId.ToString());
        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync($"Saga:{sagaTelemetryContext.SagaId}:SpanId",
            activity.Context.SpanId.ToString());

    }


    public Task EndSagaAsync(SagaTelemetryContext sagaTelemetryContext, SagaOutcome outcome)
    {
        throw new NotImplementedException();
    }

    public async Task StartOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName)
    {
        // Retrieve the parent saga's trace and span ID if you need to create a linked or child span
        var traceId = await sagaTelemetryContext.TelemetryDataPersistence.RetrieveDataAsync($"Saga:{sagaTelemetryContext.SagaId}:TraceId");
        var spanId = await sagaTelemetryContext.TelemetryDataPersistence.RetrieveDataAsync($"Saga:{sagaTelemetryContext.SagaId}:SpanId");
        
        // Creating a new activity for the operation.
        var parentContext = new ActivityContext(ActivityTraceId.CreateFromString(traceId), ActivitySpanId.CreateFromString(spanId), ActivityTraceFlags.Recorded);
        var activity = _activitySource.StartActivity($"Operation-{operationName}", ActivityKind.Internal, parentContext);

        if (activity == null)
        {
            return;
        }

        // Set initial tags or details for the activity
        activity.SetTag("operation.name", operationName);
        activity.SetTag("saga.id", sagaTelemetryContext.SagaId);

        // Store this operation's activity ID
        // along with the saga ID to be able to link or query them later
        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync(
            $"Saga:{sagaTelemetryContext.SagaId}:Operation:{operationName}:TraceId", activity.Context.TraceId.ToString());
        await sagaTelemetryContext.TelemetryDataPersistence.StoreDataAsync(
            $"Saga:{sagaTelemetryContext.SagaId}:Operation:{operationName}:SpanId", activity.Context.SpanId.ToString());
    }

    public Task EndOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, OperationOutcome outcome)
    {
        throw new NotImplementedException();
    }

    public Task RecordRetryAttemptAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, int attemptNumber)
    {
        throw new NotImplementedException();
    }

    public Task RecordCustomEventAsync(SagaTelemetryContext sagaTelemetryContext, string eventName, IDictionary<string, object>? properties = null)
    {
        throw new NotImplementedException();
    }

    public Task RecordExceptionAsync(SagaTelemetryContext sagaTelemetryContext, Exception exception, string? context = null)
    {
        throw new NotImplementedException();
    }

    public Task ActivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext)
    {
        //The saga tarted a long-running operation, we need to close all open spans (the operations and the saga)
        //And mark them as pending. When the Saga Deactivate Long Operation, we can recreate linked spans for the Saga 
        //And the operations
    }

    public Task DeactivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext)
    {

    }
}