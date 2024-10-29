using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Sagaway;


public partial class Saga<TEOperations> where TEOperations : Enum
{
    internal class SagaOperationExecution
    {
        #region Transient State - built on each activation

        private readonly Saga<TEOperations> _saga;
        private IReadOnlyList<SagaOperationExecution>? _precondition;
        private readonly ILogger _logger;

        #endregion //Transient State - built on each activation

        #region Persistent State - kept in the state store

        private readonly SagaDoAction _sagaDoAction;
        private readonly OnActionFailure _sagaRevertAction;
        private SagaAction _currentAction;
        private bool _started;

        #endregion //Persistent State - kept in the state store


        public SagaOperationExecution(Saga<TEOperations> saga, SagaOperation operation, ILogger logger)
        {
            _saga = saga;
            Operation = operation;
            _logger = logger;

            _sagaDoAction = new SagaDoAction(saga, operation, logger);
            _sagaRevertAction = new OnActionFailure(saga, operation, logger);
            _currentAction = _sagaDoAction;
        }

        public SagaOperation Operation { get; }

        public void SetDependencies(IReadOnlyList<SagaOperationExecution> precondition)
        {
            _precondition = precondition;
        }

        //the operation has been executed successfully and the saga operation is completed
        public bool Succeeded => _sagaDoAction.Succeeded;

        //the operation has been executed and failed with all retries, but not yet reverted
        public bool Failed => _sagaDoAction.Failed;

        //the operation has been executed and reverted with all retries
        public bool Reverted => _sagaRevertAction.Succeeded;

        //the operation has been executed and failed with all retries and failed to revert with all retries
        public bool RevertFailed => _sagaRevertAction.Failed;

        private bool InProgress => !Succeeded && Reverted && !RevertFailed;

        public bool CanExecute => (_started && InProgress) ||
                                  !_started && ((_precondition == null) ||
                                                !_started && _precondition.All(o => o.Succeeded));

        public bool NotStarted => !_started;

        protected void LogAndRecord(string message)
        {
            _logger.LogInformation(message);
            _saga.RecordMessage(message);
        }

        public async Task StartExecuteAsync()
        {
            if (_started)
                return;

            _started = true;
            await _currentAction.ExecuteAsync();
        }

        public async Task RevertAsync()
        {
            _logger.LogInformation("Reverting operation {Operation}", Operation.Operation);

            _currentAction = _sagaRevertAction;

            if (Reverted || RevertFailed)
            {
                await _currentAction.CancelReminderIfOnAsync();

                _logger.LogInformation("RevertAsync: Operation {Operation} already reverted or revert failed", Operation.Operation);
                return;
            }

            await _sagaRevertAction.ExecuteAsync();
        }

        public async Task InformSuccessOperationAsync()
        {
            //since Inform comes from the user of the saga, do inform may come after we start reverting
            if (_currentAction == _sagaDoAction)
            {
                await _sagaDoAction.InformSuccessOperationAsync();
            }
        }

        public async Task InformSuccessUndoOperationAsync()
        {
            //since Inform comes from the user of the saga, an undo inform may come not in order
            if (_currentAction == _sagaRevertAction)
            {
                await _sagaRevertAction.InformSuccessOperationAsync();
            }
        }

        public async Task InformFailureOperationAsync(bool failFast)
        {
            //since Inform comes from the user of the saga, do inform may come after we start reverting
            if (_currentAction == _sagaDoAction)
            {
                await _sagaDoAction.InformFailureOperationAsync(failFast);
            }
        }

        public async Task InformFailureUndoOperationAsync()
        {
            //since Inform comes from the user of the saga, an undo inform may come not in order
            if (_currentAction == _sagaRevertAction)
            {
                await _sagaRevertAction.InformFailureOperationAsync(false);
            }
        }

        public void MarkSucceeded()
        {
            _sagaDoAction.MarkSucceeded();
        }

        public void MarkReverted()
        {
            _sagaRevertAction.MarkSucceeded();
        }

        public async Task OnReminderAsync()
        {
            await _currentAction.OnReminderAsync();
        }

        public void StoreState(JsonObject json)
        {
            _logger.LogTrace("Storing state for operation {Operation}", Operation.Operation);

            var op = Operation.Operation.ToString();
            json[op] = new JsonObject();
            json[op]!["started"] = _started;
            json[op]!["IsCurrentOperationRevert"] = _currentAction == _sagaRevertAction;

            var jsonDo = new JsonObject();
            json[op + "Do"] = jsonDo;
            _sagaDoAction.StoreState(jsonDo);

            var jsonRevert = new JsonObject();
            json[op + "Revert"] = jsonRevert;
            _sagaRevertAction.StoreState(jsonRevert);
        }

        public void LoadState(JsonObject json)
        {
            try
            {
                var op = Operation.Operation.ToString();
                if (!json.ContainsKey(op))
                {
                    _logger.LogTrace("No state found for operation {Operation}", Operation.Operation);
                    return;
                }

                //else
                var opState = json[op] as JsonObject;
                _started = opState!["started"]?.GetValue<bool>() ??
                           throw new Exception("Error when loading state, missing started entry");

                var isCurrentOperationRevert = opState["IsCurrentOperationRevert"]?.GetValue<bool>() ??
                                               throw new Exception(
                                                   "Error when loading state, missing IsCurrentOperationRevert entry");
                _currentAction = isCurrentOperationRevert ? _sagaRevertAction : _sagaDoAction;

                var opDoState = json[op + "Do"] as JsonObject;
                _sagaDoAction.LoadState(opDoState!);

                var opRevertState = json[op + "Revert"] as JsonObject;
                _sagaRevertAction.LoadState(opRevertState!);

                _logger.LogTrace("State loaded for operation {Operation}", Operation.Operation);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when loading operation state");
                throw new CorruptedSagaStateException("Error when loading operation state", e);
            }
        }

        public string GetStatus()
        {
            if (Succeeded) return "Succeeded";
            if (Failed) return "Failed";
            if (Reverted) return "Reverted";
            if (RevertFailed) return "RevertFailed";
            return "Not Started";
        }
        
        // Cancel a reminder of an operation as a cleanup operation for left over reminders
        public async Task CancelPossibleReminderIfOnAsync()
        {
            try
            {
                _logger.LogInformation("Canceling possible reminder left for operation {Operation}", Operation.Operation);
                await _sagaDoAction.CancelReminderIfOnAsync();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error when trying to force canceling a possible do operation reminder");
            }
            
            try
            {
                await _sagaRevertAction.CancelReminderIfOnAsync();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error when trying to force canceling a possible revert operation reminder");
            }
        }
    }
}