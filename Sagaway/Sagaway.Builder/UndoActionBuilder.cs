// ReSharper disable once CheckNamespace
namespace Sagaway;

public partial class Saga<TEOperations> where TEOperations : Enum
{
    public partial class SagaBuilder
    {
        public partial class SagaDoOperationBuilder
        {
            /// <summary>
            /// Adds an undo action to the saga operation.
            /// </summary>
            public class UndoActionBuilder
            {
                private readonly SagaDoOperationBuilder _sagaDoOperationBuilder;
                private readonly SagaBuilder _sagaBuilder;
                private const int MaxUndoActions = 10;
                    
                internal UndoActionBuilder(SagaDoOperationBuilder sagaDoOperationBuilder, SagaBuilder sagaBuilder)
                {
                    _sagaDoOperationBuilder = sagaDoOperationBuilder;
                    _sagaBuilder = sagaBuilder;
                }

                /// <summary>
                /// The number of retries to undo the operation, if there was an undo failure
                /// </summary>
                /// <param name="undoMaxRetries">The number of retries to undo the operation, if there was an undo failure</param>
                /// <returns>The fluent interface</returns>
                public UndoActionBuilder WithMaxRetries(int undoMaxRetries)
                {
                    _sagaDoOperationBuilder._sagaOperation.RevertMaxRetries = undoMaxRetries;
                    return this;
                }

                /// <summary>
                /// The interval between undo retries
                /// </summary>
                /// <param name="undoRetryInterval">The time span between undo retry calls. Default to <see cref="MaxUndoActions"/></param>
                /// <returns></returns>
                public UndoActionBuilder WithUndoRetryInterval(TimeSpan undoRetryInterval = default)
                {
                    _sagaDoOperationBuilder._sagaOperation.RevertRetryInterval = undoRetryInterval == default ? TimeSpan.FromSeconds(MaxUndoActions) : undoRetryInterval;
                    return this;
                }

                /// <summary>
                /// The function to check if the undo operation succeeded
                /// </summary>
                /// <param name="undoValidateAsync">Should return true if the undo operation succeeded. Used on timeout cases</param>
                /// <returns></returns>
                public UndoActionBuilder WithValidateFunction(Func<Task<bool>> undoValidateAsync)
                {
                    _sagaDoOperationBuilder._sagaOperation.RevertValidateAsync = undoValidateAsync;
                    return this;
                }

                /// <summary>
                /// Add an operation to the saga
                /// </summary>
                /// <param name="operation">The operation need a do action. Undo action is optional</param>
                /// <returns>A builder to add the do action</returns>
                public SagaDoOperationBuilder WithOperation(TEOperations operation)
                {
                    return new SagaDoOperationBuilder(operation, _sagaBuilder);
                }

                /// <summary>
                /// Build the saga
                /// </summary>
                public ISaga<TEOperations> Build()
                {
                    return _sagaDoOperationBuilder.Build();
                }
            }
        }
    }
}