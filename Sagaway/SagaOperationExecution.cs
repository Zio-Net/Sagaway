using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Sagaway
{

    public partial class Saga<TEOperations> where TEOperations : Enum
    {
        internal class SagaOperationExecution
        {
            private readonly Saga<TEOperations> _saga;
            private IReadOnlyList<SagaOperationExecution>? _precondition;
            private readonly ILogger _logger;
            private bool _started;

            private readonly SagaDoAction _sagaDoAction;
            private readonly OnActionFailure _sagaRevertAction;
            private SagaAction _currentAction;

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

            public bool CanExecute => (_started && InProgress) || !_started && ((_precondition == null) || !_started && _precondition.All(o => o.Succeeded));

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
                _currentAction = _sagaRevertAction;
                await _currentAction.CancelReminderIfOnAsync();

                if (Reverted || RevertFailed)
                    return;

                await _sagaRevertAction.ExecuteAsync();
            }

            public async Task InformSuccessOperationAsync()
            {
                //since Inform comes from the user of the saga, a do inform may come after we start reverting
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
                //since Inform comes from the user of the saga, a do inform may come after we start reverting
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
                var op = Operation.Operation.ToString();
                json[op] = new JsonObject();
                json[op]!["started"] = _started;

                var jsonDo = new JsonObject();
                json[op + "Do"] = jsonDo;
                _sagaDoAction.StoreState(jsonDo);

                var jsonRevert = new JsonObject();
                json[op + "Revert"] = jsonRevert;
                _sagaRevertAction.StoreState(jsonRevert);
            }

            public void LoadState(JsonObject json)
            {
                var op = Operation.Operation.ToString();
                if (!json.ContainsKey(op)) 
                    return;
                //else
                var opState = json[op] as JsonObject;
                _started = opState!["started"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing started entry");
                var opDoState = json[op + "Do"] as JsonObject;
                _sagaDoAction.LoadState(opDoState!);

                var opRevertState = json[op + "Revert"] as JsonObject;
                _sagaRevertAction.LoadState(opRevertState!);
            }
        }
    }
}