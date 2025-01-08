using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace Sagaway;

public partial class Saga<TEOperations> where TEOperations : Enum
{
    /// <summary>
    /// The saga fluent interface builder
    /// </summary>
    public partial class SagaBuilder
    {
        private readonly string _sagaUniqueId;
        private readonly ISagaSupport _sagaSupportOperations;
        private readonly ILogger _logger;
        private Action<string>? _onSuccessCompletionCallback;
        private Action<string>? _onRevertedCallback;
        private Action<string>? _onFailedRevertedCallback;
        private readonly List<SagaOperation> _operations = new();
        private IStepRecorder? _stepRecorder;
        private Action<string>? _onFailedCallback;

        /// <summary>
        /// The saga fluent interface builder
        /// </summary>
        /// <param name="uniqueId">A unique identifier that represent the saga</param>
        /// <param name="sagaSupportOperations">Provide the required methods for managing a Saga</param>
        /// <param name="logger">The logger that the saga uses</param>
        internal SagaBuilder(string uniqueId, ISagaSupport sagaSupportOperations, ILogger logger)
        {
            _sagaUniqueId = uniqueId;
            _sagaSupportOperations = sagaSupportOperations;
            _logger = logger;
        }

        /// <summary>
        /// Add an action that is called on success completion of the saga
        /// </summary>
        /// <param name="onSuccessCompletionCallback">The callback action. The function receives the complete saga log</param>
        /// <returns>The saga builder for fluent build</returns>
        public SagaBuilder WithOnSuccessCompletionCallback(Action<string> onSuccessCompletionCallback)
        {
            _onSuccessCompletionCallback = onSuccessCompletionCallback;
            return this;
        }


        /// <summary>
        /// Add an action that is called on a failure of the saga before any compensation is handle, if exists
        /// </summary>
        /// <param name="onFailedCallback">The callback action. The function receives the partial saga log</param>
        /// <returns>The saga builder for fluent build</returns>
        public SagaBuilder WithOnFailedCallback(Action<string> onFailedCallback)
        {
            _onFailedCallback = onFailedCallback;
            return this;
        }

        /// <summary>
        /// Add an action that is called on a failure of the saga after all compensations (reverts) are done
        /// </summary>
        /// <param name="onRevertedCallback">The callback action. The function receives the complete saga log</param>
        /// <returns>The saga builder for fluent build</returns>
        /// <remarks>At least one operation failed, all retries are done.  
        /// The Saga has finished the compensation (revert) process</remarks>
        public SagaBuilder WithOnRevertedCallback(Action<string> onRevertedCallback)
        {
            _onRevertedCallback = onRevertedCallback;
            return this;
        }

        /// <summary>
        /// Add an action that is called on a failure in reverting the saga when doing compensations
        /// </summary>
        /// <param name="onFailedRevertedCallback">The callback action. The function receives the saga log</param>
        /// <returns>The saga builder for fluent build</returns>
        /// <remarks>At least one operation failed, all retries are done, but at least one operation failed to revert.  
        /// The Saga has finished the compensation (revert) process</remarks>
        public SagaBuilder WithOnFailedRevertedCallback(Action<string> onFailedRevertedCallback)
        {
            _onFailedRevertedCallback = onFailedRevertedCallback;
            return this;
        }

        /// <summary>
        /// Add an external step recorder to the saga
        /// </summary>
        /// <remarks>If the step recorder is not provided, Sagaway uses actor state based step recorder</remarks>
        /// <param name="stepRecorder"></param>
        /// <returns>The saga builder for fluent build</returns>
        public SagaBuilder WithStepRecorder(IStepRecorder stepRecorder)
        {
            _stepRecorder = stepRecorder;
            return this;
        }

        /// <summary>
        /// Add an empty step recorder that does nothing
        /// </summary>
        /// <remarks>If the step recorder is not provided, Sagaway uses actor state based step recorder</remarks>
        /// <returns>The saga builder for fluent build</returns>
        public SagaBuilder WithNullStepRecorder()
        {
            _stepRecorder = new NullStepRecorder();
            return this;
        }


        /// <summary>
        /// Add an operation to the saga
        /// </summary>
        /// <param name="operation">The operation need a do action. Undo action is optional</param>
        /// <returns>A builder to add the do action</returns>
        public SagaDoOperationBuilder WithOperation(TEOperations operation)
        {
            return new SagaDoOperationBuilder(operation, this);
        }

        private void AddOperation(SagaOperation operation)
        {
            _operations.Add(operation);
        }

        /// <summary>
        /// Build the saga
        /// </summary>
        /// <returns>The saga instance</returns>
        public ISaga<TEOperations> Build()
        {
            var saga = new Saga<TEOperations>(_logger, _sagaUniqueId, _sagaSupportOperations, _stepRecorder, _operations, _onSuccessCompletionCallback, _onFailedCallback ,_onRevertedCallback, _onFailedRevertedCallback);
            return saga;
        }
    }
}