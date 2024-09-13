using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using Sagaway.Telemetry;

namespace Sagaway;

    public partial class Saga<TEOperations> where TEOperations : Enum
    {
        internal abstract class SagaAction
        {
            #region Transient State - built on each activation

            private readonly Saga<TEOperations> _saga;
            private readonly ILogger _logger;

            #endregion //Transient State - built on each activation


            #region Persistent State - kept in the state store

            private int _retryCount;
            private ReminderType _reminderType = ReminderType.None;
            private long _lastOperationStartInUnixTimeSeconds;
            private readonly SagaOperation _sagaOperation; //persisted in another class
            protected Saga<TEOperations> Saga => _saga; //persisted in another class

            //the operation has been executed successfully 
            public bool Succeeded { get; private set; }
            //the operation has been executed and failed with all retries
            public bool Failed { get; private set; }

            #endregion //Persistent State - kept in the state store


            // ReSharper disable once ConvertToPrimaryConstructor
            protected SagaAction(Saga<TEOperations> saga, SagaOperation sagaOperation, ILogger logger)
            {
                _saga = saga;
                _sagaOperation = sagaOperation;
                _logger = logger;
            }

            protected SagaOperation SagaOperation => _sagaOperation;

            protected abstract bool IsRevert { get; }

            protected abstract TimeSpan GetRetryInterval(int retryIteration);

            protected abstract Task ExecuteActionAsync();

            protected abstract int MaxRetries { get; }

            protected abstract Task OnActionFailureAsync();
            
            protected abstract Task<bool> ValidateAsync();
            
            private string RevertText => IsRevert ? "Revert " : string.Empty;

            private string ReminderName => $"{_sagaOperation.Operation}:Retry";

            private string OperationName => $"{RevertText}{_sagaOperation.Operation}";
            
            protected void LogAndRecord(string message)
            {
                _logger.LogInformation(message);
                _saga.RecordStep(_sagaOperation.Operation, message);
            }

            public void StoreState(JsonObject json)
            {
                json["reminderType"] = (int)_reminderType;
                json["retryCount"] = _retryCount;
                json["succeeded"] = Succeeded;
                json["failed"] = Failed;
                json["lastOperationStartInUnixTimeSeconds"] = _lastOperationStartInUnixTimeSeconds;
            }

            public void LoadState(JsonObject json)
            {
                _reminderType = json["reminderType"]?.GetValue<ReminderType>() ?? throw new Exception("Error when loading state, missing isReminderOn entry");
                _retryCount = json["retryCount"]?.GetValue<int>() ?? throw new Exception("Error when loading state, missing retryCount entry");
                Succeeded = json["succeeded"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing succeeded entry");
                Failed = json["failed"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing failed entry");
                _lastOperationStartInUnixTimeSeconds = json["lastOperationStartInUnixTimeSeconds"]?.GetValue<long>() ?? throw new Exception("Error when loading state, missing lastOperationStartInUnixTimeSeconds entry");
            }


            private async Task ResetReminderAsync(ReminderType reminderType, bool immediate = false)
            {
                await CancelReminderIfOnAsync();

                TimeSpan reminderDelay = TimeSpan.FromSeconds(4); //wait minimum 4 seconds until execution

                if (!immediate)
                {

                    var nextRetryInterval = GetRetryInterval(_retryCount);

                    if (nextRetryInterval == default || MaxRetries == 0)
                    {
                        return;
                    }

                    //calculate the next retry time according to the time elapse since the last operation start, but give a reminder minimum time to wait before firig
                    //this code has the chance to exist free the lock before the reminder comes
                    reminderDelay = TimeSpan.FromSeconds(Math.Max(4,
                        nextRetryInterval.TotalSeconds - (DateTimeOffset.UtcNow.ToUnixTimeSeconds() -
                                                          _lastOperationStartInUnixTimeSeconds)));

                    LogAndRecord(reminderDelay == nextRetryInterval
                        ? $"Registering reminder {ReminderName} for {OperationName} with interval {reminderDelay}"
                        : $"Registering reminder {ReminderName} for {OperationName} with configured interval of {nextRetryInterval}, waiting the {reminderDelay} remaining seconds");
                }

                await _saga._sagaSupportOperations.SetReminderAsync(ReminderName, reminderDelay);
                _reminderType = reminderType;
            }

            private async Task ExecuteAsync()
            {
                LogAndRecord($"Start Executing {OperationName}");
                TimeSpan retryInterval = default;

                try
                {
                    _lastOperationStartInUnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (_retryCount > 0)
                    {
                        await _saga.RecordRetryAttemptTelemetryAsync(_sagaOperation.Operation, _retryCount, IsRevert);
                    }
                    await ExecuteActionAsync();
                }
                catch (Exception ex)
                {
                    LogAndRecord(
                        $"Error when calling {OperationName}. Error: {ex.Message}. Retry in {retryInterval} seconds");
                    await _saga.RecordTelemetryExceptionAsync(ex, $"Error when calling {OperationName}");
                    await InformFailureOperationAsync(false);
                }
            }

            public async Task CancelReminderIfOnAsync(bool forceCancel = false)
            {
                if (_reminderType != ReminderType.None || forceCancel)
                {
                    _logger.LogInformation($"Canceling old reminder {ReminderName} for {OperationName}");
                    _reminderType = ReminderType.None;
                    await _saga._sagaSupportOperations.CancelReminderAsync(ReminderName);
                }
            }
            
            public async Task InformFailureOperationAsync(bool failFast)
            {
                await CancelReminderIfOnAsync();

                if (Succeeded || Failed)
                {
                    return;
                }

                if (failFast)
                {
                    _logger.LogWarning($"The Operation {OperationName} Failed fast, reverting Saga");
                }
                else
                {
                    _logger.LogInformation($"Operation {OperationName} Failed");
                }

                ++_retryCount;

                if (!failFast && _retryCount <= MaxRetries)
                {
                    LogAndRecord($"Retry {OperationName}. Retry count: {_retryCount}");

                    //set a reminder for the next retry
                    await ResetReminderAsync(SagaOperation.ValidateAsync == null
                        ? ReminderType.Execute
                        : ReminderType.Validate);
                    return;
                }

                Failed = true;
                LogAndRecord(failFast ? $"{OperationName} Failed Fast." : $"{OperationName} Failed. Retries exhausted.");
                await _saga.RecordEndOperationTelemetry(_sagaOperation.Operation, 
                    IsRevert ? OperationOutcome.RevertFailed : OperationOutcome.Failed, IsRevert);

                await OnActionFailureAsync();
            }

            public async Task InformSuccessOperationAsync()
            {
                await CancelReminderIfOnAsync();

                if (Succeeded || Failed)
                {
                    return;
                }

                LogAndRecord($"{OperationName} Success");
                
                Succeeded = true;
                
                await _saga.RecordEndOperationTelemetry(_sagaOperation.Operation, IsRevert ? OperationOutcome.Reverted : OperationOutcome.Succeeded, IsRevert);
                await _saga.CheckForCompletionAsync();
            }

            public async Task OnReminderAsync()
            {
                var reminderType = _reminderType;
                await CancelReminderIfOnAsync();

                LogAndRecord("Wake by a reminder");

                if (Succeeded || Failed)
                {
                    return;
                }

                try
                {
                    if (reminderType == ReminderType.Execute)
                    {
                        await ExecuteAsync();
                        return;
                    }
                    //else

                    //try to get the action state by calling a state check function if exists
                    var hasValidated = await ValidateAsync();

                    if (hasValidated)
                    {
                        await InformSuccessOperationAsync();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogAndRecord($"Error when calling {OperationName} validate. Error: {ex.Message}.");
                }
                //the state is unknown, retry action
                LogAndRecord($"The validate function does not exist or raised an exception, retry {OperationName} action");
                await InformFailureOperationAsync(false);
            }
            
            public void MarkSucceeded()
            {
                Succeeded = true;
            }

            public async Task ScheduleExecuteAsync()
            {
            await ResetReminderAsync(ReminderType.Execute, true);
        }
        }
    }