// ReSharper disable once CheckNamespace
namespace Sagaway;

public partial class Saga<TEOperations> where TEOperations : Enum
{
    public partial class SagaBuilder
    {
        /// <summary>
        /// Provide the means to add saga operations
        /// </summary>
        public partial class SagaDoOperationBuilder
        {
            private readonly SagaOperation _sagaOperation;
            private readonly SagaBuilder _sagaBuilder;
            private const int RetryTimeout = 10;

            /// <summary>
            /// The saga operation builder
            /// </summary>
            /// <param name="operation">The operation to add</param>
            /// <param name="sagaBuilder">The builder </param>
            internal SagaDoOperationBuilder(TEOperations operation, SagaBuilder sagaBuilder)
            {
                _sagaOperation = new SagaOperation(operation);
                _sagaBuilder = sagaBuilder;
                _sagaBuilder.AddOperation(_sagaOperation);
            }

            /// <summary>
            /// Add a saga operation
            /// </summary>
            /// <param name="doOperationAsync">The action</param>
            /// <returns>The Undo section builder for the operation</returns>
            public SagaDoOperationBuilder WithDoOperation(Func<Task> doOperationAsync)
            {
                _sagaOperation.DoOperationAsync = doOperationAsync;
                return this;
            }

            /// <summary>
            /// Make sure all precondition operations run before this operation
            /// </summary>
            /// <param name="preconditions">The operations that need to be executed before</param>
            /// <returns></returns>
            public SagaDoOperationBuilder WithPreconditions(TEOperations preconditions)
            {
                _sagaOperation.Preconditions = preconditions;
                return this;
            }

            /// <summary>
            /// Set the maximum retry count for the operation
            /// </summary>
            /// <param name="maxRetries">Optional: How many times to retry the operation on a failure.</param>
            /// <returns>The fluent interface</returns>
            public SagaDoOperationBuilder WithMaxRetries(int maxRetries)
            {
                _sagaOperation.MaxRetries = maxRetries;
                return this;
            }

            /// <summary>
            /// Set the retry pause time. The default is <see cref="RetryTimeout"/>
            /// </summary>
            /// <param name="retryInterval">The time between retries. Default to <see cref="RetryTimeout"/>.</param>
            /// <returns>The fluent interface</returns>
            public SagaDoOperationBuilder WithRetryIntervalTime(TimeSpan retryInterval = default)
            {
                _sagaOperation.RetryInterval = retryInterval == default ? TimeSpan.FromSeconds(RetryTimeout) : retryInterval;
                return this;
            }

            /// <summary>
            /// Used to check if the operation succeeded on timeout
            /// </summary>
            /// <param name="validateAsync">The function should return true if the do operation succeeded.</param>
            /// <returns>The fluent interface</returns>
            public SagaDoOperationBuilder WithValidateFunction(Func<Task<bool>>? validateAsync)
            {
                _sagaOperation.ValidateAsync = validateAsync;
                return this;
            }

            /// <summary>
            /// Add a saga undo operation
            /// </summary>
            /// <param name="undoOperationAsync">The undo action</param>
            /// <returns>The Undo section builder for the operation</returns>
            public UndoActionBuilder WithUndoOperation(Func<Task> undoOperationAsync)
            {
                _sagaOperation.RevertOperationAsync = undoOperationAsync;
                return new UndoActionBuilder(this, _sagaBuilder);
            }

            /// <summary>
            /// Use if there is no undo action
            /// </summary>
            /// <returns>The saga builder to continue build the saga</returns>
            public SagaBuilder WithNoUndoAction()
            {
                return _sagaBuilder;
            }

            /// <summary>
            /// Build the saga
            /// </summary>
            public ISaga<TEOperations> Build()
            {
                return _sagaBuilder.Build();
            }
        }
    }
}