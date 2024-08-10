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
    #region Transient State - built in each activation

    private ILogger _logger;
    private readonly ISagaSupport _sagaSupportOperations;
    private readonly Action<string>? _onSuccessCallback;
    private readonly Action<string>? _onFailedCallback;
    private readonly Action<string>? _onRevertedCallback;
    private readonly Action<string>? _onRevertFailureCallback;
    private bool _deactivated;
    private readonly ILockWrapper _lock;
    private bool _resetSagaState;

    private string SagaStateName => $"Saga_{_sagaUniqueId}";

    #endregion //Transient State

    #region Persistent State - kept in the persistence store

    private string _sagaUniqueId;
    private bool _done;
    private readonly List<SagaOperationExecution> _operations;
    private readonly StringBuilder _stepRecorder = new();
    private bool _hasFailedReported;
    private bool _isReverting;
    private readonly SagaTelemetryContext _telemetryContext;
    

    #endregion //Persistent State

    /// <summary>
    /// All operations have been executed successfully and the saga is completed
    /// </summary>
    public bool Succeeded => _operations.All(o => o.Succeeded);

    /// <summary>
    /// The saga has failed and is in the process of reverting
    /// </summary>
    public bool Failed => _operations.Any(o => o.Failed);

    /// <summary>
    /// The saga has failed and has reverted all operations
    /// </summary>
    public bool Reverted => _operations.All(o => o.Reverted);

    /// <summary>
    /// The saga has failed and has failed to revert all operations. It is considered done.
    /// </summary>
    public bool RevertFailed => _operations.Any(o => o.RevertFailed) && _operations.All(o => o.RevertFailed || o.Reverted);

    /// <summary>
    /// The saga is in progress
    /// </summary>
    public bool InProgress => Started && !Succeeded && !Reverted && !RevertFailed;

    /// <summary>
    /// The saga has not started yet
    /// </summary>
    public bool NotStarted => _operations.All(o => o.NotStarted);

    /// <summary>
    /// The saga has not started
    /// </summary>
    public bool Started => !NotStarted;

    /// <summary>
    /// The Saga executed and finished either successfully or failed
    /// </summary>
    public bool Completed => !InProgress && Started;

    #region Telemetry

    private ITelemetryAdapter TelemetryAdapter => _sagaSupportOperations.TelemetryAdapter;

    private async Task RecordStartOperationTelemetry(TEOperations sagaOperation, bool isReverting)
    {
        try
        {
            await TelemetryAdapter.StartOperationAsync(_telemetryContext, (isReverting ? "Revert" : "") + sagaOperation);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error recording start operation telemetry");
        }
    }

    private async Task RecordEndOperationTelemetry(TEOperations sagaOperation, OperationOutcome operationOutcome,
        bool isReverting)
    {
        try
        {
            await TelemetryAdapter.EndOperationAsync(_telemetryContext, (isReverting ? "Revert" : "") + sagaOperation,
                operationOutcome);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error recording end operation telemetry");
        }
    }

    private async Task RecordRetryAttemptTelemetryAsync(TEOperations sagaOperationOperation, int retryCount, bool isReverting)
    {
        try
        {
            await TelemetryAdapter.RecordRetryAttemptAsync(_telemetryContext,
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
    public async Task RecordCustomTelemetryEventAsync(string eventName, IDictionary<string, object>? properties)
    {
        try
        {
            await TelemetryAdapter.RecordCustomEventAsync(_telemetryContext, eventName, properties);
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
    public async Task RecordTelemetryExceptionAsync(Exception exception, string? context = null)
    {
        try
        {
            await TelemetryAdapter.RecordExceptionAsync(_telemetryContext, exception, context);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error recording exception");
        }
    }

    #endregion //telemetry

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
        bool completed = false;
        
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
            if (_done)
            {
                _logger.LogInformation($"Saga {_sagaUniqueId} is already completed, no need to activate.");
                completed = true;
            }
        });

        if (completed)
            return;

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
            try
            {
                _stepRecorder.AppendLine("The Saga is deactivated.");
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
        try
        {
            var json = await _sagaSupportOperations.LoadSagaAsync(SagaStateName);

            //log the json as readable text
            _logger.LogDebug($"On loading state: Saga {SagaStateName} state: {json}");

            if (json is null || json.Count == 0)
            {
                _logger.LogInformation($"State {SagaStateName} is not found in persistence store, Assuming first run.");
                return true;
            }

            var uniqueId = json["sagaUniqueId"]?.GetValue<string>();

            _sagaUniqueId = uniqueId!;
            _done = json["done"]?.GetValue<bool>() ?? false;
            _isReverting = json["isReverting"]?.GetValue<bool>() ?? false;
            _hasFailedReported = json["hasFailedReported"]?.GetValue<bool>() ?? false;

            _stepRecorder.Length = 0;
            _stepRecorder.Append(json["stepRecorder"]?.GetValue<string>() ?? string.Empty);
            _stepRecorder.AppendLine("The Saga is activated.");
            foreach (var operation in _operations)
            {
                operation.LoadState(json);
            }

            var telemetryStateStore = json["telemetryStateStore"]?.GetValue<string>() ?? string.Empty;
            var telemetryStatePairs = telemetryStateStore.Split('|', StringSplitOptions.RemoveEmptyEntries);

            _telemetryStateStore.Clear();
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
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error loading saga state {SagaStateName}");

            await CancelOperationRemindersAsync();
            throw new CorruptedSagaStateException("Error loading Saga State, see inner exception. Important, check this problem, it can hurt reliability since a Saga process may stop in the middle!", ex);
        }
    }

    private async Task CancelOperationRemindersAsync()
    {
         _logger.LogInformation($"Trying to cancel all reminders for saga {_sagaUniqueId}");


         foreach (var operation in _operations)
         {
             try
             {
                 await operation.CancelPossibleReminderIfOnAsync();
             }

             catch (Exception e)
             {
                 _logger.LogWarning(e,
                     $"Error trying to cancel reminder for operation {operation.Operation.Operation} as a clean operation to prevent endless loop");
             }
         }
    }

    private async Task StoreStateAsync()
    {
        var json = new JsonObject();

        if (_resetSagaState)
        {
            //store an empty state to reset the saga
            await _sagaSupportOperations.SaveSagaStateAsync(SagaStateName, json);
            _resetSagaState = false;
            _logger.LogInformation($"Saga {SagaStateName} state is reset.");
            return;

        }
        foreach (var operation in _operations)
        {
            operation.StoreState(json);
        }

        json["sagaUniqueId"] = _sagaUniqueId;
        json["done"] = _done;
        json["isReverting"] = _isReverting;
        json["hasFailedReported"] = _hasFailedReported;
        json["stepRecorder"] = _stepRecorder.ToString();
        
        var telemetryStateStore = _telemetryStateStore.Aggregate(
            new StringBuilder(), (sb, pair) => sb.Append($"{pair.Key},{pair.Value}|"), sb => sb.ToString());

        if (!string.IsNullOrWhiteSpace(telemetryStateStore))
            json["telemetryStateStore"] = telemetryStateStore;

        await _sagaSupportOperations.SaveSagaStateAsync(SagaStateName, json);

        //log the json as readable text
        _logger.LogDebug($"On storing state: Saga {SagaStateName} state: {json}");
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
        bool ShouldRun() => !_deactivated && (NotStarted || InProgress);

        await _lock.LockAsync(async () =>
        {
            while (ShouldRun())
            {
                var allWaitingOperations = _operations.Where(o => o.CanExecute).ToList();

                if (!allWaitingOperations.Any() || allWaitingOperations.All(o => o.Failed))
                    return;

                foreach (var operation in allWaitingOperations)
                {
                    await operation.StartExecuteAsync();
                    if (!ShouldRun())
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
         //we use _done flag and not the Completed property to make sure we enter this function
        //for the last time when the saga is done
        if (_done) 
            return;

        var recordedSteps = _stepRecorder.ToString();

        try
        {
            //Failed is a transient state, so the saga is not done reverting,
            //We ensure that we call the onFailedCallback only once
            if (Failed && _onFailedCallback != null && !_hasFailedReported)
            {
                _hasFailedReported = true;
                _onFailedCallback(recordedSteps);
                TelemetryAdapter.RecordCustomEventAsync(_telemetryContext, "SagaFailure");
            }

            if (Completed)
            {
                _done = true;
                var telemetryOutcome = Succeeded ? SagaOutcome.Succeeded :
                    Reverted ? SagaOutcome.Reverted : SagaOutcome.PartiallyReverted;
                TelemetryAdapter.EndSagaAsync(_telemetryContext, telemetryOutcome);
            }

            //first handle completion notification
            if (Succeeded && _onSuccessCallback != null)
            {
                _onSuccessCallback(recordedSteps);
            } 
            else if (Reverted && _onRevertedCallback != null)
            {
                _onRevertedCallback(recordedSteps);
            }
            else if (RevertFailed && _onRevertFailureCallback != null)
            {
                _onRevertFailureCallback(recordedSteps);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling report result callback");
        }

        if (!_done) //saga completed
        {
            _logger.LogInformation($"Saga {_sagaUniqueId} is still in progress.");
            return;
        }

        //saga is done

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
            await Task.CompletedTask;
            //on updating store, we will erase the state
            _resetSagaState = true;
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
