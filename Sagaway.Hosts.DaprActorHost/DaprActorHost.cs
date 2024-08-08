using Dapr.Actors.Runtime;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json.Nodes;
using Grpc.Net.Client;
using Sagaway.Callback.Router;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sagaway.Hosts.DaprActorHost;
using Sagaway.Telemetry;
using System.Diagnostics;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable once CheckNamespace
namespace Sagaway.Hosts;

/// <summary>
/// A Sagaway Saga Dapr Actor host
/// </summary>
/// <typeparam name="TEOperations">The enum of the saga operations</typeparam>
[DebuggerTypeProxy(typeof(DaprActorHostDebuggerProxy<>))]
public abstract class DaprActorHost<TEOperations> : Actor, IRemindable, ISagaSupport, ISagawayActor
    where TEOperations : Enum
{
    private readonly ILogger _logger;
    private DaprClient? _daprClient;
    private readonly ITelemetryAdapter _telemetryAdapter;

    /// <summary>
    /// Create a Dapr Actor host for the saga
    /// </summary>
    /// <param name="host">The Dapr actor host</param>
    /// <param name="logger">The injected logger</param>
    /// <param name="serviceProvider">Enable advanced features injections such as open telemetry.
    /// It is an optional parameter for backward compatibility support</param>
    protected DaprActorHost(ActorHost host, ILogger logger, IServiceProvider? serviceProvider = null)
        : base(host)
    {
        ActorHost = host;
        _logger = logger;
        _telemetryAdapter = serviceProvider?.GetService<ITelemetryAdapter>() ?? new NullTelemetryAdapter();
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
    /// You can create any non actor related resource. Any resource that needs actor state should be created between
    /// <see cref="OnActivateSagaAsync"/> and <see cref="OnDeactivateSagaAsync"/>
    /// The call recreate the Saga operations and delicate the call to the saga
    /// to load the last stored saga state.
    /// </summary>
    protected sealed override Task OnActivateAsync()
    {
        if (Saga != null)
        {
            _logger.LogWarning("OnActivateAsync called when saga is already active, doing nothing.");
            return Task.CompletedTask;
        }

        //else    
        _logger.LogInformation($"Activating actor id: {Id}");

        CreateDaprClient();

        return Task.CompletedTask;
    }

    protected override async Task OnPreActorMethodAsync(ActorMethodContext actorMethodContext)
    {
        Saga = null; //just in case
        Saga = ReBuildSaga();
        Saga.OnSagaCompleted += async (_, _) => await OnSagaCompletedAsync();
        await Saga.InformActivatedAsync();
        await OnActivateSagaAsync();
    }

    protected override async Task OnPostActorMethodAsync(ActorMethodContext actorMethodContext)
    {
        _logger.LogInformation($"Deactivating actor id: {Id}");
        await OnDeactivateSagaAsync();
        await Saga!.InformDeactivatedAsync();
        Saga = null;
    }

    /// <summary>
    /// Create the HttpClient to use for the actor
    /// </summary>
    /// <returns>A http client</returns>
    protected virtual HttpClient CreateHttpClient()
    {
        // Default implementation uses DaprClient's default HttpClient configuration
        //return new HttpClient(new CustomInterceptorHandler());
        return new HttpClient();
    }

    private void CreateDaprClient()
    {
        var httpClient = CreateHttpClient(); // Get the custom or default HttpClient
        httpClient.DefaultRequestHeaders.Add("x-sagaway-dapr-actor-id", ActorHost.Id.GetId());
        httpClient.DefaultRequestHeaders.Add("x-sagaway-dapr-actor-type", ActorHost.ActorTypeInfo.ActorTypeName);
        httpClient.DefaultRequestHeaders.Add("x-sagaway-callback-binding-name", GetCallbackBindingName());
        
        var daprClientBuilder = new DaprClientBuilder();

        daprClientBuilder.UseGrpcChannelOptions(new GrpcChannelOptions()
        {
            HttpClient = httpClient
        });

        _daprClient = daprClientBuilder.Build();
    }

    protected DaprClient DaprClient => _daprClient!;

    /// <summary>
    /// Get the callback binding name that the actor uses to receive messages
    /// from target services
    /// </summary>
    protected abstract string GetCallbackBindingName();

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
    /// Called when the saga is deactivated, before the saga state is stored
    /// Override this method to store any Saga state that is not stored by the framework
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
        //Saga ??= ReBuildSaga();
        await Saga!.RunAsync();
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
    /// Provide the telemetry adapter for the saga
    /// </summary>
    ITelemetryAdapter ISagaSupport.TelemetryAdapter => _telemetryAdapter;


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
    /// The Dapr Actor ensures a single call at a time, so no need for a lock
    /// </summary>
    /// <returns></returns>
    public ILockWrapper CreateLock()
    {
        return new NonLockAsync();
    }

    /// <summary>
    /// Inform the outcome of an operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="isSuccessful">Success or failure</param>
    /// <param name="failFast">If true, fail the Saga, stop retries and start revert</param>
    /// <returns>Async operation</returns>
    [Obsolete("Use ReportOperationOutcomeAsync with the FastOutcome enum instead")]
    protected async Task ReportCompleteOperationOutcomeAsync(TEOperations operation, bool isSuccessful,
        bool failFast)
    {
        await Saga!.ReportOperationOutcomeAsync(operation, isSuccessful, failFast);
    }

    /// <summary>
    /// Implementer should call this method to inform the outcome of an operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="isSuccessful">Success or failure</param>
    /// <param name="sagaFastOutcome">Inform a fast outcome for the Saga from a single operation, either fast fail or success
    /// <remarks><see cref="SagaFastOutcome.Failure"/> fails the saga and start the compensation process</remarks>
    /// <remarks><see cref="SagaFastOutcome.Success"/> Finish the saga successfully, marked all non-started operations as succeeded</remarks></param>
    /// <returns>Async operation</returns>
    protected async Task ReportCompleteOperationOutcomeAsync(TEOperations operation, bool isSuccessful,
        SagaFastOutcome sagaFastOutcome = SagaFastOutcome.None)
    {
        await Saga!.ReportOperationOutcomeAsync(operation, isSuccessful, sagaFastOutcome);
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

    /// <summary>
    /// Enable the actor to provide the deserialization json options for the callback payload
    /// </summary>
    /// <returns></returns>
    protected virtual JsonSerializerOptions GetJsonSerializerOptions()
    {
        // Provide default options that can be used across most derived classes
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
    }

    public async Task ResetSagaAsync()
    {
        if (Saga != null)
        {
            await ((ISagaReset)Saga).ResetSagaAsync();
        }
    }

    /// <summary>
    /// Used by the framework to dispatch callbacks to the actor
    /// </summary>
    public async Task DispatchCallbackAsync(string payloadJson, string methodName)
    {
        try
        {
            MethodInfo? methodInfo = GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (methodInfo == null)
            {
                _logger.LogError($"Method {methodName} not found in actor {this.GetType().Name}.");
                throw new InvalidOperationException($"Method {methodName} not found.");
            }

            // Validate method accepts exactly one parameter
            var parameters = methodInfo.GetParameters();
            if (parameters.Length != 1)
            {
                _logger.LogError($"Method {methodName} does not accept exactly one parameter.");
                throw new InvalidOperationException($"Method {methodName} signature mismatch: expected exactly one parameter.");
            }

            var parameterType = parameters.First().ParameterType;

            // Deserialize the payload to the expected parameter type
            var parameter = JsonSerializer.Deserialize(payloadJson, parameterType, GetJsonSerializerOptions());
            if (parameter == null)
            {
                _logger.LogError($"Unable to deserialize payload to type {parameterType.Name} for method {methodName}.");
                throw new InvalidOperationException($"Payload deserialization failed for method {methodName}.");
            }

            // Dynamically invoke the method with the deserialized parameter
            var result = methodInfo.Invoke(this, [parameter]);

            // If the method is asynchronous, await the returned Task
            if (result is Task task)
            {
                await task;
            }
        }
        catch (TargetInvocationException tie)
        {
            _logger.LogError(tie, $"Error invoking method {methodName}.");
            throw tie.InnerException ?? tie;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error dispatching callback to method {methodName}.");
            throw;
        }
    }

    /// <summary>
    /// Easy way to get the callback metadata
    /// </summary>
    /// <param name="callbackMethodName"></param>
    /// <returns>the target callback function metadata</returns>
    /// <exception cref="ArgumentException"></exception>
    protected Dictionary<string, string> GetCallbackMetadata(string callbackMethodName)
    {
        if (string.IsNullOrEmpty(callbackMethodName))
        {
            _logger.LogInformation("Callback method name is null or empty.");
            throw new ArgumentException("Callback method name cannot be null or empty.", nameof(callbackMethodName));
        }

        return new Dictionary<string, string>
        {
            { "x-sagaway-callback-method", callbackMethodName },
            { "x-sagaway-message-dispatch-time", DateTime.UtcNow.ToString("o")} // ISO 8601 format
        };
    }

    /// <summary>
    /// Return the saga log up to this point
    /// </summary>
    public string SagaLog => Saga?.SagaLog ?? "The Saga object is null";

    /// <summary>
    /// Return the saga status for debugging purposes
    /// </summary>
    /// <returns>The complete state of the Saga</returns>
    public string GetSagaStatus()
    {
        return Saga?.GetSagaStatus() ?? "The Saga object is null";
    }
}