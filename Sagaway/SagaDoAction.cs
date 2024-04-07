using Microsoft.Extensions.Logging;

namespace Sagaway
{
    public partial class Saga<TEOperations> where TEOperations : Enum
    {
        internal class SagaDoAction : SagaAction
        {
            // ReSharper disable once ConvertToPrimaryConstructor
            public SagaDoAction(Saga<TEOperations> saga, SagaOperation sagaOperation, ILogger logger) 
                : base(saga, sagaOperation, logger)
            {
            }

            protected override bool IsRevert => false;

            protected override TimeSpan GetRetryInterval(int retryIteration) =>
                SagaOperation.RetryIntervalFunction?.Invoke(retryIteration) ?? SagaOperation.RetryInterval;

            protected override int MaxRetries => SagaOperation.MaxRetries;

            protected override async Task ExecuteActionAsync()
            {
                if (SagaOperation.DoOperationAsync != null) 
                    await SagaOperation.DoOperationAsync();
            }

            protected override async Task OnActionFailureAsync()
            {
                await Saga.CompensateAsync();
            }

            protected override async Task<bool> ValidateAsync()
            {
                return  await (SagaOperation.ValidateAsync?.Invoke() ?? Task.FromResult(false));
            }
        }
    }
}