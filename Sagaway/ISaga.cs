﻿namespace Sagaway;

public interface ISaga<in TEOperations> where TEOperations : Enum
{
    /// <summary>
    /// Execute the saga
    /// </summary>
    /// <returns>Async method</returns>
    Task RunAsync();

    /// <summary>
    /// Call when all saga operations are completed
    /// </summary>
    event EventHandler<SagaCompletionEventArgs> OnSagaCompleted;

    /// <summary>
    /// The saga has not started yet
    /// </summary>
    public bool NotStarted { get; }

    /// <summary>
    /// The saga has not started
    /// </summary>
    public bool Started { get; }

    /// <summary>
    /// The saga is in progress
    /// </summary>
    bool InProgress { get; }

    /// <summary>
    /// All operations have been executed successfully and the saga is completed
    /// </summary>
    // ReSharper disable once UnusedMemberInSuper.Global
    bool Succeeded { get; }

    /// <summary>
    /// The saga has failed and is in the process of reverting
    /// </summary>
    // ReSharper disable once UnusedMemberInSuper.Global
    bool Failed { get; }

    /// <summary>
    /// The saga has failed and has reverted all operations
    /// </summary>
    // ReSharper disable once UnusedMemberInSuper.Global
    bool Reverted { get; }

    /// <summary>
    /// The saga has failed and has failed to revert all operations. It is considered done.
    /// </summary>
    // ReSharper disable once UnusedMemberInSuper.Global
    bool RevertFailed { get; }

    /// <summary>
    /// The Saga executed and finished either successfully or failed
    /// </summary>
    bool Completed { get; }

    /// <summary>
    /// Implementer should call this method to inform an activated event
    /// </summary>
    /// <param name="isCalledFromReminder"></param>default is false, if true, the reminder is calling the activation
    /// <param name="afterLoadCallback">Callback will be called after saga state loaded, before restarting execution</param>
    /// <returns>Async operation</returns>
    Task InformActivatedAsync(Func<Task>? afterLoadCallback = null, bool isCalledFromReminder = false);

    /// <summary>
    /// Implementer should call this method to inform a deactivated event
    /// </summary>
    /// <returns>Async operation</returns>
    Task InformDeactivatedAsync();

    /// <summary>
    /// Implementer should call this method to inform the outcome of an operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="success">Success or failure</param>
    /// <param name="failFast">If true, fail the Saga, stop retries and start revert</param>
    /// <returns>Async operation</returns>
    [Obsolete("Use ReportOperationOutcomeAsync with the FastOutcome enum instead")]
    Task ReportOperationOutcomeAsync(TEOperations operation, bool success, bool failFast);

    /// <summary>
    /// Implementer should call this method to inform the outcome of an operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="success">Success or failure</param>
    /// <param name="sagaFastOutcome">Inform a fast outcome for the Saga from a single operation, either fast fail or success
    /// <remarks><see cref="SagaFastOutcome.Failure"/> fails the saga and start the compensation process</remarks>
    /// <remarks><see cref="SagaFastOutcome.Success"/> Finish the saga successfully, marked all non-started operations as succeeded</remarks></param>
    /// <returns>Async operation</returns>
    Task ReportOperationOutcomeAsync(TEOperations operation, bool success, SagaFastOutcome sagaFastOutcome = SagaFastOutcome.None);

    /// <summary>
    ///  Implementer should call this method to inform the outcome of an undo operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="success">Success or failure</param>
    /// <returns>Async operation</returns>
    Task ReportUndoOperationOutcomeAsync(TEOperations operation, bool success);

    /// <summary>
    /// Call the saga to handle reminder operations
    /// </summary>
    /// <param name="reminder"></param>
    /// <returns>Async operation</returns>
    Task ReportReminderAsync(string reminder);

    /// <summary>
    /// Return the saga log up to this point
    /// </summary>
    public string SagaLog { get; }

    /// <summary>
    /// Return the saga status for debugging purposes
    /// </summary>
    /// <returns>The complete state of the Saga</returns>
    public string GetSagaStatus();

    /// <summary>
    /// Record custom telemetry event that is part of the Saga execution and can be traced using
    /// services such as OpenTelemetry
    /// </summary>
    /// <param name="eventName">The custom event name</param>
    /// <param name="properties">The custom event parameters</param>
    Task RecordCustomTelemetryEventAsync(string eventName, IDictionary<string, object>? properties);


    /// <summary>
    /// Records any exceptions or failures as part of the Saga operation open telemetry span
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="context">An optional context or description where the exception occurred.</param>
    Task RecordTelemetryExceptionAsync(Exception exception, string? context);
}