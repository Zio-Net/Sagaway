// ReSharper disable once CheckNamespace
namespace Sagaway;

public partial class Saga<TEOperations> where TEOperations : Enum
{
    internal record SagaOperation(TEOperations Operation)
    {
        public TEOperations? Preconditions { get; set; } 
        public Func<Task>? DoOperationAsync { get; set; } = null;
        public int MaxRetries { get; set; }
        public TimeSpan RetryInterval { get; set; }
        public Func<Task<bool>>? ValidateAsync { get; set; } 
        public Func<Task>? RevertOperationAsync { get; set; }
        public int RevertMaxRetries { get; set; } = 0;
        public TimeSpan RevertRetryInterval { get; set; }
        public Func<Task<bool>>? RevertValidateAsync { get; set; }
        public Func<int, TimeSpan>? RetryIntervalFunction { get; set; }
        public Func<int, TimeSpan>? RevertRetryIntervalFunction { get; internal set; }
    }
}