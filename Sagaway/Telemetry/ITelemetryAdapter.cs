namespace Sagaway.Telemetry;

/// <summary>
/// Defines an interface for telemetry operations within a Saga,
/// allowing for the capture of execution times, operation outcomes, retries, and exceptions.
/// The method is called by the Saga to record telemetry events and metrics.
/// The call is not under lock, so the implementation should be thread-safe.
/// </summary>
public interface ITelemetryAdapter
{
    /// <summary>
    /// Marks the start of a Saga, capturing the start time.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    Task StartSagaAsync(SagaTelemetryContext sagaTelemetryContext);

    /// <summary>
    /// Marks the completion of a Saga, capturing the end time and outcome.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    /// <param name="outcome">The outcome of the Saga.</param>
    Task EndSagaAsync(SagaTelemetryContext sagaTelemetryContext, SagaOutcome outcome);

    /// <summary>
    /// Marks the start of an operation within a Saga.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    /// <param name="operationName">The name of the operation.</param>
    Task StartOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName);

    /// <summary>
    /// Marks the completion of an operation within a Saga, capturing the execution time and outcome.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="outcome">The outcome of the operation.</param>
    Task EndOperationAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, OperationOutcome outcome);

    /// <summary>
    /// Records a retry attempt for an operation within a Saga.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    /// <param name="operationName">The name of the operation being retried.</param>
    /// <param name="attemptNumber">The retry attempt number.</param>
    Task RecordRetryAttemptAsync(SagaTelemetryContext sagaTelemetryContext, string operationName, int attemptNumber);

    /// <summary>
    /// Captures custom events or metrics as needed.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    /// <param name="eventName">The name of the custom event.</param>
    /// <param name="properties">The properties and values associated with the event.</param>
    Task RecordCustomEventAsync(SagaTelemetryContext sagaTelemetryContext, string eventName, IDictionary<string, object>? properties = null);

    /// <summary>
    /// Records any exceptions or failures not explicitly captured by the standard methods.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="context">An optional context or description where the exception occurred.</param>
    Task RecordExceptionAsync(SagaTelemetryContext sagaTelemetryContext, Exception exception, string? context = null);

    /// <summary>
    /// Signals the start of a potentially long-running operation within the Saga.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    Task ActivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext);

    /// <summary>
    /// Signals the end of a potentially long-running operation within the Saga,
    /// allowing the telemetry adapter to finalize any pending telemetry data.
    /// </summary>
    /// <param name="sagaTelemetryContext">The saga context with the saga id, type, and persistence storage</param>
    Task DeactivateLongOperationAsync(SagaTelemetryContext sagaTelemetryContext);
}