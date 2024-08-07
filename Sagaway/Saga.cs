using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sagaway.Telemetry;

namespace Sagaway;

/// <summary>
/// The saga implementation class
/// </summary>
/// <typeparam name="TEOperations"></typeparam>
/// <remarks>Use <see cref="SagaBuilder"></see> to build a saga instance/> </remarks>
public partial class Saga<TEOperations> : ISagaReset, ISaga<TEOperations> where TEOperations : Enum
{
    private ILogger _logger;
    private string _sagaUniqueId;
    private readonly ISagaSupport _sagaSupportOperations;
    private readonly List<SagaOperationExecution> _operations;
    private readonly Action<string>? _onSuccessCallback;
    private readonly Action<string>? _onFailedCallback;
    private readonly Action<string>? _onRevertedCallback;
    private readonly Action<string>? _onRevertFailureCallback;
    private bool _isReverting;
    private readonly StringBuilder _stepRecorder = new();
    private bool _deactivated;
    private bool _hasFailedReported;
    private readonly ILockWrapper _lock;
    private readonly SagaTelemetryContext _telemetryContext;


    private string SagaStateName => $"Saga_{_sagaUniqueId}";

    #region Telemetry

    private ITelemetryAdapter TelemetryAdapter => _sagaSupportOperations.TelemetryAdapter;

    private void RecordStartOperationTelemetry(TEOperations sagaOperation, bool isReverting)
    {
        try
        {
            TelemetryAdapter.StartOperationAsync(_telemetryContext, (isReverting ? "Revert" : "") + sagaOperation);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error recording start operation telemetry");
        }
    }

    private void RecordEndOperationTelemetry(TEOperations sagaOperation, OperationOutcome operationOutcome,
        bool isReverting)
    {
        try
        {
            TelemetryAdapter.EndOperationAsync(_telemetryContext, (isReverting ? "Revert" : "") + sagaOperation,
                operationOutcome);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error recording end operation telemetry");
        }
    }

    private void RecordRetryAttemptTelemetry(TEOperations sagaOperationOperation, int retryCount, bool isReverting)
    {
        try
        {
            TelemetryAdapter.RecordRetryAttemptAsync(_telemetryContext,
                (isReverting ? "Revert" : "") + sagaOperationOperation, retryCount);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error recording retry attempt telemetry");
        }
    }

    /// <summary>
    /// Record custom telemetry event that is part of the Saga execution and can be traced using
    /// services such as OpenTelemetry
    /// </summary>
    /// <param name="eventName">The custom event name</param>
    /// <param name="properties">The custom event parameters</param>
    protected void RecordCustomTelemetryEvent(string eventName, IDictionary<string, object> properties)
    {
        try
        {
            TelemetryAdapter.RecordCustomEventAsync(_telemetryContext, eventName, properties);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error recording custom telemetry event");
        }
    }

    /// <summary>
    /// Records any exceptions or failures.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="context">An optional context or description where the exception occurred.</param>
    // ReSharper disable once MemberCanBePrivate.Global
    protected void RecordException(Exception exception, string? context = null)
    {
        try
        {
            TelemetryAdapter.RecordExceptionAsync(_telemetryContext, exception, context);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error recording exception");
        }
    }

    #endregion //telemetry

    /// <summary>
    /// The saga is in progress
    /// </summary>
    public bool InProgress { get; private set; } = true;

    /// <summary>
    /// All operations have been executed successfully and the saga is completed
    /// </summary>
    public bool Succeeded { get; private set; }

    /// <summary>
    /// The saga has failed and is in the process of reverting
    /// </summary>
    public bool Failed { get; private set; }

    /// <summary>
    /// The saga has failed and has reverted all operations
    /// </summary>
    public bool Reverted { get; private set; }

    /// <summary>
    /// The saga has failed and has failed to revert all operations. It is considered done.
    /// </summary>
    public bool RevertFailed { get; private set; }

    /// <summary>
    /// The Saga executed and finished either successfully or failed
    /// </summary>
    public bool Completed { get; private set; }

    private Saga(ILogger logger, string sagaUniqueId, ISagaSupport sagaSupportOperations,
        IReadOnlyList<SagaOperation> operations, Action<string>? onSuccessCallback, Action<string>? onFailedCallback,
        Action<string>? onRevertedCallback, Action<string>? onRevertFailureCallback)
    {
        _logger = logger;
        _sagaUniqueId = sagaUniqueId;
        _sagaSupportOperations = sagaSupportOperations;
        _operations = operations.Select(o => new SagaOperationExecution(this, o, logger)).ToList();
        _onSuccessCallback = onSuccessCallback;
        _onFailedCallback = onFailedCallback;
        _onRevertedCallback = onRevertedCallback;
        _onRevertFailureCallback = onRevertFailureCallback;
        _lock = _sagaSupportOperations.CreateLock();

        Validate();
        SetExecutableOperationDependencies();
        _telemetryContext = new SagaTelemetryContext(_sagaUniqueId, $"Saga{typeof(TEOperations).Name}",
            logger, new TelemetryDataPersistence(this));
    }

    private void Validate()
    {
        //must provide logger
        if (_logger == null)
        {
            throw new ValidationException(nameof(_logger));
        }

        //saga unique id must be provided
        if (string.IsNullOrWhiteSpace(_sagaUniqueId))
        {
            throw new ValidationException("A unique saga id must be provided for the Saga");
        }

        //saga support must be provided
        if (_sagaSupportOperations == null)
        {
            throw new ValidationException(
                $"A saga support type the implement the interface {nameof(ISagaSupport)} must be provided");
        }

        //at least one operation must be provided
        if (_operations == null || _operations.Count == 0)
        {
            throw new ValidationException("At least one operation must be provided");
        }

        //for all provided operations, a do operation must be provided
        if (_operations.Any(o => o.Operation.DoOperationAsync == null))
        {
            throw new ValidationException("All operation must set the Do function");
        }

        if (_operations.Count == 0)
        {
            throw new ValidationException("No operations are defined");
        }

        //must have a success callback
        if (_onSuccessCallback == null)
        {
            throw new ValidationException("No on success callback is defined");
        }

        //must have a revert callback
        if (_onRevertedCallback == null && _onFailedCallback == null)
        {
            throw new ValidationException("No on reverted or on failed callback is defined");
        }

        //must have a revert failure callback
        if (_onRevertFailureCallback == null && _onFailedCallback == null)
        {
            throw new ValidationException("No on revert failure or on failed callback is defined");
        }

        //for each operation that provided a validate function, a Retry number that is grater than 0 must be provided
        if (_operations.Any(o => o.Operation is { ValidateAsync: { }, MaxRetries: <= 0 }))
        {
            throw new ValidationException(
                "Retry count must be greater than 0 for all operations that provide a validate function");
        }

        //for each operation that provided a validate function, a positive  Retry Interval or retry function must be provided
        if (_operations.Any(o => o.Operation is
                { ValidateAsync: not null, RetryInterval.TotalSeconds: <= 0, RetryIntervalFunction: null }))
        {
            throw new ValidationException(
                "Retry interval must be greater than 0 or a retry delay function is provided for all operations that provide a validate function");
        }
    }

    //called by the ctor
    private void SetExecutableOperationDependencies()
    {
        foreach (var operation in _operations)
        {
            var dependencies = _operations.Where(o => operation.Operation.Preconditions!.HasFlag(o.Operation.Operation))
                .ToList();
            operation.SetDependencies(dependencies);
        }
    }

    /// <summary>
    /// Create a saga fluent interface builder
    /// </summary>
    /// <param name="uniqueId"></param>
    /// <param name="sagaSupportOperations"></param>
    /// <param name="logger"></param>
    /// <returns>A saga builder</returns>
    public static SagaBuilder Create(string uniqueId, ISagaSupport sagaSupportOperations, ILogger logger)
    {
        return new SagaBuilder(uniqueId, sagaSupportOperations, logger);
    }

    /// <summary>
    /// Handles the activated event
    /// </summary>
    /// <returns>Async operation</returns>
    public async Task InformActivatedAsync()
    {
        bool isNew = false;

        await _lock.LockAsync(async () =>
        {
            _deactivated = false;
            try
            {
                isNew = await LoadStateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading saga state {SagaStateName}");
                throw;
            }

            if (!InProgress)
            {
                throw new InvalidOperationException("Saga is not in progress");
            }
        });

        await TelemetryAdapter.StartSagaAsync(_telemetryContext, isNew);
    }

    /// <summary>
    /// Handles the deactivated event
    /// </summary>
    /// <returns>Async operation</returns>
    public async Task InformDeactivatedAsync()
    {
        await _lock.LockAsync(async () =>
        {
            if (!InProgress)
            {
                _logger.LogInformation($"Saga {_sagaUniqueId} is already completed, no need to deactivate.");
                return;
            }

            try
            {
                _stepRecorder.AppendLine($"{SagaStateName} is deactivated.");
                _deactivated = true;
                await StoreStateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving saga state {SagaStateName}");
                throw;
            }
        });

        await TelemetryAdapter.ActivateLongOperationAsync(_telemetryContext);
    }

    //load the state of the saga, if there is no state, it is a new saga
    private async Task<bool> LoadStateAsync()
    {
        var json = await _sagaSupportOperations.LoadSagaAsync(SagaStateName);
        if (json is null || json.Count == 0)
        {
            _logger.LogInformation($"State {SagaStateName} is not found in persistence store, Assuming first run.");
            return true;
        }

        var uniqueId = json["sagaUniqueId"]?.GetValue<string>();

        _sagaUniqueId = uniqueId!;
        _isReverting = json["isReverting"]?.GetValue<bool>() ?? false;
        _hasFailedReported = json["hasFailedReported"]?.GetValue<bool>() ?? false;
        _stepRecorder.Length = 0;
        _stepRecorder.Append(json["stepRecorder"]?.GetValue<string>() ?? string.Empty);
        _stepRecorder.AppendLine($"{SagaStateName} is activated.");
        foreach (var operation in _operations)
        {
            operation.LoadState(json);
        }

        var telemetryStateStore = json["telemetryStateStore"]?.GetValue<string>() ?? string.Empty;
        var telemetryStatePairs = telemetryStateStore.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in telemetryStatePairs)
        {
            var keyValue = pair.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (keyValue.Length == 2)
            {
                _telemetryStateStore[keyValue[0]] = keyValue[1];
            }
        }

        CheckForCompletion();
        return false; //new saga
    }

    private async Task StoreStateAsync()
    {
        var json = new JsonObject();

        foreach (var operation in _operations)
        {
            operation.StoreState(json);
        }

        json["sagaUniqueId"] = _sagaUniqueId;
        json["isReverting"] = _isReverting;
        json["hasFailedReported"] = _hasFailedReported;
        json["stepRecorder"] = _stepRecorder.ToString();

        var telemetryStateStore = _telemetryStateStore.Aggregate(
            new StringBuilder(), (sb, pair) => sb.Append($"{pair.Key},{pair.Value}|"), sb => sb.ToString());

        if (!string.IsNullOrWhiteSpace(telemetryStateStore))
            json["telemetryStateStore"] = telemetryStateStore;

        await _sagaSupportOperations.SaveSagaStateAsync(SagaStateName, json);
    }

    /// <summary>
    /// Call when all saga operations are completed
    /// </summary>
    public event EventHandler<SagaCompletionEventArgs>? OnSagaCompleted;

    /// <summary>
    /// Execute the saga
    /// </summary>
    /// <returns>Async method</returns>
    public async Task RunAsync()
    {
        await _lock.LockAsync(async () =>
        {
            while (InProgress && !_deactivated)
            {
                var allWaitingOperations = _operations.Where(o => o.CanExecute).ToList();

                if (!allWaitingOperations.Any() || allWaitingOperations.All(o => o.Failed))
                    return;

                foreach (var operation in allWaitingOperations)
                {
                    await operation.StartExecuteAsync();
                    if (!InProgress || _deactivated)
                        break;
                }
            }
        });
    }

    /// <summary>
    /// Handles the outcome of an operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="success">Success or failure</param>
    /// <param name="failFast">If true, fail the Saga, stop retries and start revert</param>
    /// <returns>Async operation</returns>
    [Obsolete("Use ReportOperationOutcomeAsync with the FastOutcome enum instead")]
    public async Task ReportOperationOutcomeAsync(TEOperations operation, bool success, bool failFast)
    {
        await ReportOperationOutcomeAsync(operation, success,
            failFast ? SagaFastOutcome.Failure : SagaFastOutcome.None);
    }

    /// <summary>
    /// Implementer should call this method to inform the outcome of an operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="success">Success or failure</param>
    /// <param name="sagaFastOutcome">Inform a fast outcome for the Saga from a single operation, either fast fail or success
    /// <remarks><see cref="SagaFastOutcome.Failure"/> fails the saga and start the compensation process</remarks>
    /// <remarks><see cref="SagaFastOutcome.Success"/> Finish the saga successfully, marked all non-started operations as succeeded</remarks></param>
    /// <returns>Async operation</returns>
    public async Task ReportOperationOutcomeAsync(TEOperations operation, bool success,
        SagaFastOutcome sagaFastOutcome = SagaFastOutcome.None)
    {
        if (success && sagaFastOutcome == SagaFastOutcome.Failure)
            throw new InvalidOperationException("Cannot have success and fail fast at the same time");

        if (!success && sagaFastOutcome == SagaFastOutcome.Success)
            throw new InvalidOperationException("Cannot have fast success without success");

        await _lock.LockAsync(async () =>
        {
            if (!InProgress)
                return;

            try
            {
                if (sagaFastOutcome == SagaFastOutcome.Success)
                {

                    var allNotStartedOperations = _operations.Where(o => o.NotStarted);

                    foreach (var op in allNotStartedOperations)
                    {
                        op.MarkSucceeded();
                    }
                }

                var operationExecution = _operations.Single(o => o.Operation.Operation.Equals(operation));
                if (success)
                {
                    await operationExecution.InformSuccessOperationAsync();
                }
                else
                {
                    await operationExecution.InformFailureOperationAsync(sagaFastOutcome == SagaFastOutcome.Failure);
                }
            }
            finally
            {
                await RunAsync();
            }
        });
    }

    /// <summary>
    /// Handles the outcome of an undo operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="success">Success or failure</param>
    /// <returns>Async operation</returns>
    public async Task ReportUndoOperationOutcomeAsync(TEOperations operation, bool success)
    {
        await _lock.LockAsync(async () =>
        {
            if (!InProgress)
                return;

            var operationExecution = _operations.Single(o => o.Operation.Operation.Equals(operation));
            if (success)
            {
                await operationExecution.InformSuccessUndoOperationAsync();
            }
            else
            {
                await operationExecution.InformFailureUndoOperationAsync();
            }
        });
    }

    /// <summary>
    /// Return the saga log up to this point
    /// </summary>
    public string SagaLog => _stepRecorder.ToString();


    private void CheckForCompletion()
    {
        if (!InProgress)
            return;

        Succeeded = _operations.All(o => o.Succeeded);
        Failed = _operations.Any(o => o.Failed);
        Reverted = _operations.All(o => o.Reverted);
        RevertFailed = _operations.Any(o => o.RevertFailed) && _operations.All(o => o.RevertFailed || o.Reverted);
        InProgress = !Succeeded && !Reverted && !RevertFailed;
        Completed = !InProgress && !_operations.All(o => o.NotStarted);

        var recordedSteps = _stepRecorder.ToString();
        try
        {
            if (Succeeded && _onSuccessCallback != null)
            {
                _onSuccessCallback(recordedSteps);
            }

            if (Failed && _onFailedCallback != null && !_hasFailedReported)
            {
                _hasFailedReported = true;
                _onFailedCallback(recordedSteps);
                TelemetryAdapter.RecordCustomEventAsync(_telemetryContext, "SagaFailure");
            }

            if (Reverted && _onRevertedCallback != null)
            {
                _onRevertedCallback(recordedSteps);
            }

            if (RevertFailed && _onRevertFailureCallback != null)
            {
                _onRevertFailureCallback(recordedSteps);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling report result callback");
        }

        if (!InProgress) //saga competed
        {
            _logger.LogInformation($"Saga {_sagaUniqueId} completed with status: " +
                                   (Succeeded ? "Success" : Reverted ? "Reverted" : "RevertFailed"));

            SagaCompletionStatus sagaCompletionStatus = Succeeded ? SagaCompletionStatus.Succeeded :
                Reverted ? SagaCompletionStatus.Reverted : SagaCompletionStatus.RevertFailed;

            try
            {
                OnSagaCompleted?.Invoke(this,
                    new SagaCompletionEventArgs(_sagaUniqueId, sagaCompletionStatus, recordedSteps));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error calling OnSagaCompleted event");
            }

            var telemetryOutcome = Succeeded ? SagaOutcome.Succeeded :
                Reverted ? SagaOutcome.Reverted : SagaOutcome.PartiallyReverted;
            TelemetryAdapter.EndSagaAsync(_telemetryContext, telemetryOutcome);
        }
        else
        {
            _logger.LogInformation($"Saga {_sagaUniqueId} is still in progress.");
        }
    }

    private void RecordStep(TEOperations operation, string step)
    {
        AppendRecordStep($"[{operation}]: {step}");
    }

    private void RecordMessage(string message)
    {
        AppendRecordStep($"{message}[{_sagaUniqueId}]");
    }

    private void AppendRecordStep(string message)
    {
        var time = DateTimeOffset.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _stepRecorder.AppendLine($"[{time}]{message}");
    }

    private async Task CompensateAsync()
    {
        if (_isReverting)
        {
            _logger.LogInformation($"Saga {_sagaUniqueId} is already in reverting state.");
            return;
        }

        _isReverting = true;
        foreach (var operation in _operations)
        {
            if (operation.NotStarted)
            {
                var message =
                    $"Saga {_sagaUniqueId} is not reverting not started operation {operation.Operation.Operation}.";
                RecordStep(operation.Operation.Operation, message);
                _logger.LogInformation(message);
                operation.MarkReverted();
                continue;
            }

            await operation.RevertAsync();
        }

        CheckForCompletion();
    }

    /// <summary>
    /// Handles reminder operations
    /// </summary>
    /// <param name="reminder"></param>
    /// <returns>Async operation</returns>
    public async Task ReportReminderAsync(string reminder)
    {
        await _lock.LockAsync(async () =>
        {
            try
            {
                if (_deactivated)
                {
                    await InformActivatedAsync();
                }

                var operationName = reminder.Split(':')[0];
                var operation = _operations.Single(o => o.Operation.Operation.ToString().Equals(operationName));
                await operation.OnReminderAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling reminder {reminder}, canceling reminder.");
                await _sagaSupportOperations.CancelReminderAsync(reminder);
            }
        });
    }

    /// <summary>
    /// Reset the saga state to allow re-execution
    /// </summary>
    /// <returns>Async operation</returns>
    public async Task ResetSagaAsync()
    {
        await _lock.LockAsync(async () =>
        {
            try
            {
                //store empty state
                var json = new JsonObject();
                await _sagaSupportOperations.SaveSagaStateAsync(SagaStateName, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting saga {_sagaUniqueId}");
                throw;
            }
        });
    }

    /// <summary>
    /// Return the saga status for debugging purposes
    /// </summary>
    /// <returns>The complete state of the Saga</returns>
    public string GetSagaStatus()
    {
        var status = new StringBuilder();
        status.AppendLine($"Saga Status: {(Succeeded ? "Succeeded" : Failed ? "Failed" : Reverted ? "Reverted" : RevertFailed ? "RevertFailed" : "In Progress")}");
        status.AppendLine("Operation Statuses:");

        foreach (var operation in _operations)
        {
            status.AppendLine($"{operation.Operation.Operation}: {operation.GetStatus()}");
        }

        status.AppendLine("Saga Log:");
        status.AppendLine(SagaLog);

        return status.ToString();
    }
}
