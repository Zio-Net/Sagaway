using Microsoft.Extensions.Logging;

namespace Sagaway
{
    public partial class Saga<TEOperations> where TEOperations : Enum
    {
        internal class OnActionFailure : SagaAction
        {
            public OnActionFailure(Saga<TEOperations> saga, SagaOperation sagaOperation, ILogger logger)
                : base(saga, sagaOperation, logger)
            {
            }
            protected override bool IsRevert => true;

            protected override TimeSpan RetryInterval => SagaOperation.RevertRetryInterval;

            protected override int MaxRetries => SagaOperation.RevertMaxRetries;

            protected override async Task ExecuteActionAsync()
            {
                if (SagaOperation.RevertOperationAsync == null)
                {
                    LogAndRecord($"No undo operation for {SagaOperation.Operation}. Marking as reverted");
                    MarkSucceeded();
                    return;
                }
                //else
                await SagaOperation.RevertOperationAsync();
            }

            protected override async Task OnActionFailureAsync()
            {
                Saga.CheckForCompletion();
                await Task.CompletedTask;
            }
            
            protected override async Task<bool> ValidateAsync()
            {
                return await (SagaOperation.RevertValidateAsync?.Invoke() ?? Task.FromResult(false));
            }

            // ReSharper disable once UnusedMember.Global
            public async Task RevertAsync()
            {
                LogAndRecord($"Reverting {SagaOperation.Operation}");
                await ExecuteAsync();
            }
        }
    }
}