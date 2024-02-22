using Dapr.Actors.Runtime;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

// ReSharper disable once CheckNamespace
namespace Sagaway.Hosts
{
    /// <summary>
    /// A Sagaway Saga Dapr Actor host
    /// </summary>
    /// <typeparam name="TEOperations">The enum of the saga operations</typeparam>
    public abstract class DaprActorHost<TEOperations> : Actor, IRemindable, ISagaSupport
        where TEOperations : Enum
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Create a Dapr Actor host for the saga
        /// </summary>
        /// <param name="host">The Dapr actor host</param>
        /// <param name="logger">The injected logger</param>
        protected DaprActorHost(ActorHost host, ILogger logger)
            : base(host)
        {
            ActorHost = host;
            _logger = logger;
        }

        /// <summary>
        /// The Dapr Actor host
        /// </summary>
        protected ActorHost ActorHost { get; }

        /// <summary>
        /// The hosted saga
        /// </summary>
        /// <remarks>The saga has a value only after starting the execution process</remarks>
        protected ISaga<TEOperations>? Saga { get; private set; }

        /// <summary>
        /// Implementer should create the saga using the <see cref="Saga&lt;TEOperations&gt;.SagaBuilder"/> fluent-interface
        /// </summary>
        /// <returns>The Saga instance</returns>
        protected abstract ISaga<TEOperations> ReBuildSaga();

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// An actor is activated the first time any of its methods are invoked.
        /// The call recreate the Saga operations and delicate the call to the saga
        /// to load the last stored saga state.
        /// </summary>
        protected sealed override async Task OnActivateAsync()
        {
            if (Saga != null)
            {
                _logger.LogWarning("OnActivateAsync called when saga is already active, doing nothing.");
                return;
            }
            //else    
            _logger.LogInformation($"Activating actor id: {Id}");
            Saga = ReBuildSaga();
            Saga.OnSagaCompleted += async (_, _) => await OnSagaCompletedAsync();
            await Saga.InformActivatedAsync();
            await OnActivateSagaAsync();
        }

        //Clean Actor state on saga completion - this is just a cache cleanup.
        //The database state will be cleaned by the Actor runtime garbage collector
        private async Task OnSagaCompletedAsync()
        {
            await StateManager.ClearCacheAsync();
        }

        /// <summary>
        /// Called when the saga is activated, after the saga state is rebuilt
        /// </summary>
        /// <returns>Async operation</returns>
        protected virtual async Task OnActivateSagaAsync()
        {
            await Task.CompletedTask;
        }


        /// <summary>
        /// This method is called whenever an actor is deactivated after a period of inactivity.
        /// </summary>
        protected override async Task OnDeactivateAsync()
        {
            _logger.LogInformation($"Deactivating actor id: {Id}");
            await OnDeactivateSagaAsync();
            await Saga!.InformDeactivatedAsync();
        }


        /// <summary>
        /// Called when the saga is deactivated, before the saga state is stored
        /// </summary>
        /// <returns>Async operation</returns>
        protected virtual async Task OnDeactivateSagaAsync()
        {
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// First call to run the Saga process
        /// </summary>
        /// <remarks>This call should be tun once on the first time we need to activate the saga</remarks>
        /// <returns></returns>
        protected async Task SagaRunAsync()
        {
            Saga ??= ReBuildSaga();
            await Saga.RunAsync();
        }

        /// <summary>
        /// A function to set reminder. The reminder should bring the saga back to life and call the OnReminder function
        /// With the reminder name.
        /// </summary>
        /// <param name="reminderName">A unique name for the reminder</param>
        /// <param name="dueTime">The time to re-activate the saga</param>
        /// <returns>Async operation</returns>
        async Task ISagaSupport.SetReminderAsync(string reminderName, TimeSpan dueTime)
        {
            await RegisterReminderAsync(reminderName, null, dueTime, dueTime);
        }

        /// <summary>
        /// A function to cancel a reminder
        /// </summary>
        /// <param name="reminderName">The reminder to cancel</param>
        /// <returns>Async operation</returns>
        async Task ISagaSupport.CancelReminderAsync(string reminderName)
        {
            await UnregisterReminderAsync(reminderName);
        }

        /// <summary>
        /// Call by the Dapr Actor Host to remind about registered reminder
        /// </summary>
        /// <param name="reminderName">The name of the reminder</param>
        /// <param name="state">The saved state</param>
        /// <param name="dueTime">When</param>
        /// <param name="period">When again</param>
        /// <returns>Async operation</returns>
        /// <remarks>Call the base.ReceiveReminderAsync for all reminder that you didn't set</remarks>
        public virtual async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            await Saga!.ReportReminderAsync(reminderName);
        }

        /// <summary>
        /// Use Dapr persistence to persist the saga state
        /// </summary>
        /// <param name="sagaId">The saga unique id</param>
        /// <param name="state">The saga serialized state</param>
        /// <returns>Async operation</returns>
        async Task ISagaSupport.SaveSagaStateAsync(string sagaId, JsonObject state)
        {
            await StateManager.SetStateAsync(sagaId, state.ToString());
        }

        /// <summary>
        /// Load the saga state from Dapr persistence store
        /// </summary>
        /// <param name="sagaId">The saga unique id</param>
        /// <returns>The serialized saga state</returns>
        async Task<JsonObject?> ISagaSupport.LoadSagaAsync(string sagaId)
        {
            var stateJsonText = await StateManager.TryGetStateAsync<string>(sagaId);
            if (!stateJsonText.HasValue)
            {
                _logger.LogInformation($"Saga state not found for saga id: {sagaId}, assuming a new saga");
                return null;
            }
            var jsonObject = JsonNode.Parse(stateJsonText.Value) as JsonObject;
            return jsonObject!;
        }

        /// <summary>
        /// Inform the outcome of an operation
        /// </summary>
        /// <param name="operation">The operation</param>
        /// <param name="isSuccessful">Success or failure</param>
        /// <param name="failFast">If true, fail the Saga, stop retries and start revert</param>
        /// <returns>Async operation</returns>
        protected async Task ReportCompleteOperationOutcomeAsync(TEOperations operation, bool isSuccessful, bool failFast = false)
        {
            await Saga!.ReportOperationOutcomeAsync(operation, isSuccessful, failFast);
        }

        /// <summary>
        /// Inform the outcome of an undo operation
        /// </summary>
        /// <param name="operation">The operation</param>
        /// <param name="isSuccessful">The outcome</param>
        /// <returns>Async operation</returns>
        protected async Task ReportUndoOperationOutcomeAsync(TEOperations operation, bool isSuccessful)
        {
            await Saga!.ReportUndoOperationOutcomeAsync(operation, isSuccessful);
        }
    }
}