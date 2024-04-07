using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Sagaway
{
    public partial class Saga<TEOperations> where TEOperations : Enum
    {
        internal abstract class SagaAction
        {
            private readonly Saga<TEOperations> _saga;
            private readonly SagaOperation _sagaOperation;
            private readonly ILogger _logger;
            bool _isReminderOn;
            private int _retryCount;

            protected SagaAction(Saga<TEOperations> saga, SagaOperation sagaOperation, ILogger logger)
            {
                _saga = saga;
                _sagaOperation = sagaOperation;
                _logger = logger;
            }

            protected Saga<TEOperations> Saga => _saga;

            protected SagaOperation SagaOperation => _sagaOperation;

            protected abstract bool IsRevert { get; }

            protected abstract TimeSpan GetRetryInterval(int retryIteration);

            protected abstract Task ExecuteActionAsync();

            protected abstract int MaxRetries { get; }

            protected abstract Task OnActionFailureAsync();
            
            protected abstract Task<bool> ValidateAsync();
            
            private string RevertText => IsRevert ? "Revert " : string.Empty;

            private string ReminderName => $"{_sagaOperation.Operation}:Retry";
            
            protected void LogAndRecord(string message)
            {
                _logger.LogInformation(message);
                _saga.RecordStep(_sagaOperation.Operation, message);
            }

            public void StoreState(JsonObject json)
            {
                json["isReminderOn"] = _isReminderOn;
                json["retryCount"] = _retryCount;
            }

            public void LoadState(JsonObject json)
            {
                _isReminderOn = json["isReminderOn"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing isReminderOn entry");
                _retryCount = json["retryCount"]?.GetValue<int>() ?? throw new Exception("Error when loading state, missing retryCount entry");
            }

            private async Task<TimeSpan> ResetReminderAsync()
            {
                var retryInterval = GetRetryInterval(_retryCount);
                
                if (retryInterval == default || MaxRetries == 0)
                {
                    return default;
                }

                await CancelReminderIfOnAsync();

                LogAndRecord($"Registering reminder {ReminderName} for {RevertText}{_sagaOperation.Operation} with interval {retryInterval}");
                await _saga._sagaSupportOperations.SetReminderAsync(ReminderName, retryInterval);
                _isReminderOn = true;

                return retryInterval;
            }

            public async Task ExecuteAsync()
            {
                LogAndRecord($"Start Executing {RevertText}");
                TimeSpan retryInterval = default;

                try
                {
                    retryInterval = await ResetReminderAsync();
                    await ExecuteActionAsync(); 
                }
                catch (Exception ex)
                {
                    LogAndRecord($"Error when calling {RevertText}. Error: {ex.Message}. Retry in {retryInterval} seconds");

                    if (!_isReminderOn)
                    {
                        //no reminder and we failed. Take failure action right away
                        LogAndRecord($"No reminder set for {RevertText}{_sagaOperation.Operation}. Taking failure action");
                        await InformFailureOperationAsync(false);
                    }
                }
            }
            
            public async Task CancelReminderIfOnAsync()
            {
                if (_isReminderOn)
                {
                    _logger.LogInformation($"Canceling old reminder {RevertText}{ReminderName} for {_sagaOperation.Operation}");
                    _isReminderOn = false;
                    await _saga._sagaSupportOperations.CancelReminderAsync(ReminderName);
                }
            }
            
            public async Task InformFailureOperationAsync(bool failFast)
            {
                if (failFast)
                {
                    _logger.LogWarning($"The Operation {RevertText}Failed fast, reverting Saga");
                }
                else
                {
                    _logger.LogInformation($"Operation {RevertText}Failed");

                }

                _retryCount++;
                if (!failFast && _retryCount <= MaxRetries)
                {
                    LogAndRecord($"Retry {RevertText}operation. Retry count: {_retryCount}");
                    await ExecuteAsync();
                    return;
                }
                Failed = true;
                LogAndRecord(failFast ? $"{RevertText}Failed Fast." : $"{RevertText}Failed. Retries exhausted.");

                await OnActionFailureAsync();
            }

            public async Task InformSuccessOperationAsync()
            {
                LogAndRecord($"{RevertText}Success");
                await CancelReminderIfOnAsync();
                Succeeded = true;
                
                _saga.CheckForCompletion();
            }

            public async Task OnReminderAsync()
            {
                _isReminderOn = true;
                await CancelReminderIfOnAsync();

                LogAndRecord("Wake by a reminder");

                if (Succeeded)
                {
                    return;
                }


                try
                {
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
                    LogAndRecord($"Error when calling {RevertText}{_sagaOperation.Operation} validate. Error: {ex.Message}.");
                }
                //the state is unknown, retry action
                LogAndRecord($"The state is unknown in the reminder, retry {RevertText}action");
                await InformFailureOperationAsync(false);
            }
            
            //the operation has been executed successfully 
            public bool Succeeded { get; private set; }
            //the operation has been executed and failed with all retries
            public bool Failed { get; private set; }
            
            public void MarkSucceeded()
            {
                Succeeded = true;
            }
        }
    }
}