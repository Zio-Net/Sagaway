using Microsoft.Extensions.Logging;

namespace Sagaway
{
    public partial class Saga<TEOperations> where TEOperations : Enum
    {
        internal class OnActionFailure : SagaAction
        {
            // ReSharper disable once ConvertToPrimaryConstructor
            public OnActionFailure(Saga<TEOperations> saga, SagaOperation sagaOperation, ILogger logger)
                : base(saga, sagaOperation, logger)
            {
            }
            protected override bool IsRevert => true;


            protected override async Task ExecuteActionAsync()
            {
                if (SagaOperation.RevertOperationAsync == null)
                {
                    LogAndRecord($"No undo operation for {SagaOperation.Operation}. Marking as reverted");
                    MarkSucceeded();
                    return;
                }
                //else
                await Saga.RecordStartOperationTelemetry(SagaOperation.Operation, true);
                await SagaOperation.RevertOperationAsync();
            }

            protected override TimeSpan GetRetryInterval(int retryIteration) =>
                SagaOperation.RevertRetryIntervalFunction?.Invoke(retryIteration) ?? SagaOperation.RevertRetryInterval;

            protected override int MaxRetries => SagaOperation.RevertMaxRetries;

            protected override async Task OnActionFailureAsync()
            {
                Saga.CheckForCompletion();
                await Task.CompletedTask;
            }
            
            protected override async Task<bool> ValidateAsync()
            {
                return await (SagaOperation.RevertValidateAsync?.Invoke() ?? Task.FromResult(false));
            }
        }
    }
}