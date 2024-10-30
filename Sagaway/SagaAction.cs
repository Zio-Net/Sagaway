using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using Sagaway.Telemetry;

namespace Sagaway
{
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
                SagaOperation = sagaOperation;
                _logger = logger;

                _logger.LogTrace("Initialized {OperationName} for saga {SagaId}", sagaOperation.Operation, saga._sagaUniqueId);
            }

            protected SagaOperation SagaOperation { get; }

            protected abstract bool IsRevert { get; }

            protected abstract TimeSpan GetRetryInterval(int retryIteration);

            protected abstract Task ExecuteActionAsync();

            protected abstract int MaxRetries { get; }

            protected abstract Task OnActionFailureAsync();
            
            protected abstract Task<bool?> ValidateAsync();
            
            private string RevertText => IsRevert ? "Revert " : string.Empty;

            private string ReminderName => $"{SagaOperation.Operation}:Retry";

            private string OperationName => $"{RevertText}{SagaOperation.Operation}";
            
            protected void LogAndRecord(string message)
            {
                _logger.LogInformation(message);
                _saga.RecordStep(SagaOperation.Operation, message);
            }

            public void StoreState(JsonObject json)
            {
                json["retryCount"] = _retryCount;
                json["succeeded"] = Succeeded;
                json["failed"] = Failed;
            }

            public void LoadState(JsonObject json)
            {
                _retryCount = json["retryCount"]?.GetValue<int>() ?? throw new Exception("Error when loading state, missing retryCount entry");
                Succeeded = json["succeeded"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing succeeded entry");
                Failed = json["failed"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing failed entry");
            }

            private async Task<TimeSpan> ResetReminderAsync()
            {
                var retryInterval = GetRetryInterval(_retryCount);
                
                if (retryInterval == default || MaxRetries == 0)
                {
                    _logger.LogDebug("No reminder needed for {OperationName} in saga {SagaId} as retry interval is default or max retries is 0.", OperationName, _saga._sagaUniqueId);
                    return default;
                }

                LogAndRecord($"Registering reminder {ReminderName} for {OperationName} with interval {retryInterval}");
                await _saga._sagaSupportOperations.SetReminderAsync(ReminderName, retryInterval);


                _logger.LogTrace("Reminder {ReminderName} set for operation {OperationName} in saga {SagaId} with interval {RetryInterval}", ReminderName, OperationName, _saga._sagaUniqueId, retryInterval);

                return retryInterval;
            }

            public async Task ExecuteAsync()
            {
                LogAndRecord($"Start Executing {OperationName}");
                TimeSpan retryInterval = default;

                try
                {
                    retryInterval = await ResetReminderAsync();
                    await ExecuteActionAsync(); 
                }
                catch (Exception ex)
                {
                    LogAndRecord($"Error when calling {OperationName}. Error: {ex.Message}. Retry in {retryInterval} seconds");
                    await _saga.RecordTelemetryExceptionAsync(ex, $"Error when calling {OperationName}");

                    if (retryInterval == default)
                    {
                        //no reminder and we failed. Take failure action right away
                        LogAndRecord($"No reminder set for {OperationName}. Taking failure action");
                        await InformFailureOperationAsync(false);
                    }
                }
            }

            public async Task CancelReminderIfOnAsync()
            {
                _logger.LogInformation("Canceling old reminder {ReminderName} for {OperationName} if on.", ReminderName,
                    OperationName);
                await _saga._sagaSupportOperations.CancelReminderAsync(ReminderName);
            }

            public async Task InformFailureOperationAsync(bool failFast)
            {
                if (Succeeded || Failed)
                {
                    await CancelReminderIfOnAsync();
                    _logger.LogInformation("InformFailureOperationAsync: Operation {OperationName} already {result}. No action needed.", 
                        OperationName, Succeeded ? "succeeded" : "failed");
                    return;
                }

                if (failFast)
                {
                    _logger.LogWarning("The Operation {OperationName} Failed fast, reverting Saga", OperationName);
                }
                else
                {
                    _logger.LogInformation("Operation {OperationName} Failed", OperationName);
                }

                _retryCount++;
                if (!failFast && _retryCount <= MaxRetries)
                {
                    LogAndRecord($"Retry {OperationName}. Retry count: {_retryCount}");
                    await _saga.RecordRetryAttemptTelemetryAsync(SagaOperation.Operation, _retryCount, IsRevert);

                    await ExecuteAsync();
                    return;
                }

                Failed = true;
                LogAndRecord(failFast ? $"{OperationName} Failed Fast." : $"{OperationName} Failed. Retries exhausted.");
                await _saga.RecordEndOperationTelemetry(SagaOperation.Operation, 
                    IsRevert ? OperationOutcome.RevertFailed : OperationOutcome.Failed, IsRevert);

                await OnActionFailureAsync();
            }

            public async Task InformSuccessOperationAsync()
            {
                if (Succeeded || Failed)
                {
                    await CancelReminderIfOnAsync();

                    _logger.LogInformation("InformSuccessOperationAsync: Operation {OperationName} already {result}. No action needed.",
                        OperationName, Succeeded ? "succeeded" : "failed");
                    return;
                }

                LogAndRecord($"{OperationName} Success");
                
                Succeeded = true;

                await _saga.RecordEndOperationTelemetry(SagaOperation.Operation, IsRevert ? OperationOutcome.Reverted : OperationOutcome.Succeeded, IsRevert);
                await _saga.CheckForCompletionAsync();

            }

            public async Task OnReminderAsync()
            {
                if (Succeeded || Failed)
                {
                    await CancelReminderIfOnAsync();
                    _logger.LogInformation("OnReminderAsync: Operation {OperationName} finished as {result}. No action needed.", OperationName, Succeeded ? "succeeded" : "failed");
                    return;
                }

                LogAndRecord("Wake by a reminder");

                try
                {
                    //try to get the action state by calling a state check function if exists
                    var hasValidated = await ValidateAsync();

                    _logger.LogInformation("OnReminderAsync: Operation {OperationName} validated: {HasValidated}", OperationName, hasValidated);

                    if (hasValidated == null)
                    {
                        LogAndRecord($"OnReminderAsync: No validate function defined for {OperationName}, cannot proceed. Marking as failed.");
                        await InformFailureOperationAsync(false);
                        return;
                    }

                    if (hasValidated == true)
                    {
                        LogAndRecord($"OnReminderAsync: {OperationName} passed validation successfully.");
                        await InformSuccessOperationAsync();
                        return;
                    }
                    //else the validation returned false
                    
                    LogAndRecord($"OnReminderAsync: Validation for {OperationName} returned false, retrying action.");
                    await InformFailureOperationAsync(false);
                }
                catch (Exception ex)
                {
                    LogAndRecord($"OnReminderAsync: Error when calling {OperationName} validate. Error: {ex.Message}.");
                    await InformFailureOperationAsync(false);
                }
            }
            
            public void MarkSucceeded()
            {
                Succeeded = true;
            }
        }
    }
}