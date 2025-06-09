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
using System.Linq.Expressions;
using System.Text;
using Dapr;
using Polly;
using Polly.Retry;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable once CheckNamespace
// ReSharper disable VirtualMemberNeverOverridden.Global
// ReSharper disable  UnusedMember.Global
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
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly Random _jitterer = new();

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
        //todo: provide a way to configure the retry policy
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                10, 
                retryAttempt => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, retryAttempt))) // starting from 2 second, max 60 seconds
                    + TimeSpan.FromMilliseconds(_jitterer.Next(0, 1000)),
                (exception, timespan, retryCount, _) =>
                {
                    _logger.LogWarning(exception,
                        "DaprActorHost retry {retryCount} due to {exceptionType}: {message}. Waiting {duration} seconds before next retry.",
                        retryCount, exception.GetType().Name, exception.Message, timespan.TotalSeconds);
                });

        _logger.LogInformation("Dapr Actor host created for actor id: {Id}", Id);
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
        _logger.LogInformation("Activating actor id: {Id}", Id);

        CreateDaprClient();

        return Task.CompletedTask;
    }

    protected override async Task OnPreActorMethodAsync(ActorMethodContext actorMethodContext)
    {
        _logger.LogDebug("OnPreActorMethodAsync invoked for method {MethodName}", actorMethodContext.MethodName);

        try
        {
            Saga = null; //just in case
            Saga = ReBuildSaga();
            var isCalledFromReminder = actorMethodContext.CallType == ActorCallType.ReminderMethod;
            await Saga.InformActivatedAsync(OnActivateSagaAsync, isCalledFromReminder);

            _logger.LogInformation("Saga rebuilt and activated for actor id: {ActorId}", Id);
        }
        catch (Exception ex)
        {
            if (actorMethodContext.CallType == ActorCallType.ReminderMethod)
            {
                _logger.LogWarning(ex, "Error during reminder activation, finish gracefully to allow cancelling leftover reminders");
                return;
            }
            
            _logger.LogError(ex, "Error during saga activation");
            throw;
        }
    }

    protected override async Task OnPostActorMethodAsync(ActorMethodContext actorMethodContext)
    {
        _logger.LogInformation("Deactivating actor id: {ActorId} after executing {MethodName}", Id, actorMethodContext.MethodName);

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
        httpClient.DefaultRequestHeaders.Add("x-sagaway-dapr-callback-binding-name", GetCallbackBindingName());
        
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
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await RegisterReminderAsync(reminderName, null, dueTime, dueTime);
        });
    }

    /// <summary>
    /// A function to cancel a reminder
    /// </summary>
    /// <param name="reminderName">The reminder to cancel</param>
    /// <returns>Async operation</returns>
    async Task ISagaSupport.CancelReminderAsync(string reminderName)
    {
        try
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    await UnregisterReminderAsync(reminderName);
                }
                catch (DaprApiException daprException) when (daprException.Message.Contains("412"))
                {
                    _logger.LogWarning("Error 412: Reminder {ReminderName}. Retrying", reminderName);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not cancel reminder {ReminderName}", reminderName);
        }
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
        await _retryPolicy.ExecuteAsync(async () => { await Saga!.ReportReminderAsync(reminderName); });
    }

    /// <summary>
    /// Use Dapr persistence to persist the saga state
    /// </summary>
    /// <param name="sagaId">The saga unique id</param>
    /// <param name="state">The saga serialized state</param>
    /// <returns>Async operation</returns>
    async Task ISagaSupport.SaveSagaStateAsync(string sagaId, JsonObject state)
    {
        await _retryPolicy.ExecuteAsync(async () => { await StateManager.SetStateAsync(sagaId, state.ToString()); });
    }

    /// <summary>
    /// Load the saga state from Dapr persistence store
    /// </summary>
    /// <param name="sagaId">The saga unique id</param>
    /// <returns>The serialized saga state</returns>
    async Task<JsonObject?> ISagaSupport.LoadSagaAsync(string sagaId)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var stateJsonText = await StateManager.TryGetStateAsync<string>(sagaId);
            if (!stateJsonText.HasValue)
            {
                _logger.LogInformation("Saga state not found for saga id: {sagaId}, assuming a new saga", sagaId);
                return null;
            }

            var jsonObject = JsonNode.Parse(stateJsonText.Value) as JsonObject;
            return jsonObject!;
        });
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
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await Saga!.ReportOperationOutcomeAsync(operation, isSuccessful, 
                failFast == false ? SagaFastOutcome.Failure : SagaFastOutcome.None);
        });
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
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await Saga!.ReportOperationOutcomeAsync(operation, isSuccessful, sagaFastOutcome);
        });
    }

    /// <summary>
    /// Inform the outcome of an undo operation
    /// </summary>
    /// <param name="operation">The operation</param>
    /// <param name="isSuccessful">The outcome</param>
    /// <returns>Async operation</returns>
    protected async Task ReportUndoOperationOutcomeAsync(TEOperations operation, bool isSuccessful)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await Saga!.ReportUndoOperationOutcomeAsync(operation, isSuccessful);
        });
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

    /// <summary>
    /// Reset the saga state to allow re-execution
    /// </summary>
    /// <returns>Async operation</returns>
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
    /// <summary>
    /// Used by the framework to dispatch callbacks to the actor
    /// </summary>
    public async Task DispatchCallbackAsync(string payloadJson, string methodName)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                MethodInfo? methodInfo = GetType().GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (methodInfo == null)
                {
                    _logger.LogError("Method {methodName} not found in actor {TypeName}.", methodName, GetType().Name);
                    throw new InvalidOperationException($"Method {methodName} not found.");
                }

                // Validate method accepts exactly one parameter
                var parameters = methodInfo.GetParameters();
                if (parameters.Length != 1)
                {
                    _logger.LogError("Method {methodName} does not accept exactly one parameter.", methodName);
                    throw new InvalidOperationException(
                        $"Method {methodName} signature mismatch: expected exactly one parameter.");
                }

                var parameterType = parameters.First().ParameterType;

                // Deserialize the payload to the expected parameter type
                var parameter = JsonSerializer.Deserialize(payloadJson, parameterType, GetJsonSerializerOptions());
                if (parameter == null)
                {
                    _logger.LogError("Unable to deserialize payload to type {parameterTypeName} for method {methodName}.", 
                        parameterType.Name, methodName);
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
                _logger.LogError(tie, "Error invoking method {methodName}.", methodName);
                throw tie.InnerException ?? tie;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching callback to method {methodName}.", methodName);
                throw;
            }
        });
    }


    /// <summary>
    /// Easy way to get the callback metadata
    /// </summary>
    /// <param name="callbackMethodName"></param>
    /// <param name="customMetadata">Any additional metadata that can be flow with the call context</param>
    /// <returns>the target callback function metadata</returns>
    /// <exception cref="ArgumentException"></exception>
    protected Dictionary<string, string> GetCallbackMetadata(string callbackMethodName, string customMetadata = "")
    {
        if (string.IsNullOrEmpty(callbackMethodName))
        {
            _logger.LogInformation("Callback method name is null or empty.");
            throw new ArgumentException("Callback method name cannot be null or empty.", nameof(callbackMethodName));
        }

        return new Dictionary<string, string>
        {
            { "x-sagaway-dapr-callback-method-name", callbackMethodName },
            { "x-sagaway-dapr-message-dispatch-time", DateTime.UtcNow.ToString("o")}, // ISO 8601 format
            { "x-sagaway-dapr-custom-metadata", customMetadata}
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

    /// <summary>
    /// Record custom telemetry event that is part of the Saga execution and can be traced using
    /// services such as OpenTelemetry
    /// </summary>
    /// <param name="eventName">The custom event name</param>
    /// <param name="properties">The custom event parameters</param>
    protected async Task RecordCustomTelemetryEventAsync(string eventName, IDictionary<string, object>? properties = null)
    {
        if (Saga != null)
            await Saga.RecordCustomTelemetryEventAsync(eventName, properties);
    }

    /// <summary>
    /// Records any exceptions or failures as part of the Saga operation open telemetry span
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="context">An optional context or description where the exception occurred.</param>
    protected async Task RecordTelemetryExceptionAsync(Exception exception, string? context = null)
    {
        if (Saga != null)
            await Saga.RecordTelemetryExceptionAsync(exception, context);
    }

    /// <summary>
    /// Provide the ability to capture the callback context for the actor as a string.
    /// </summary>
    /// <param name="callbackMethodName">The callback function that will be dispatched by this context.</param>
    /// <param name="customMetadata">Any additional metadata that can be flow with the call context</param>
    /// <returns>The callback context as a key,value dictionary</returns>
    public IDictionary<string, string> CaptureCallbackContext(string callbackMethodName, string customMetadata = "")
    {
        var callbackContext = new Dictionary<string, string>()
        {
            ["x-sagaway-dapr-callback-method-name"] = callbackMethodName,
            ["x-sagaway-dapr-actor-id"] = ActorHost.Id.GetId(),
            ["x-sagaway-dapr-actor-type"] = ActorHost.ActorTypeInfo.ActorTypeName,
            ["x-sagaway-dapr-message-dispatch-time"] = DateTime.UtcNow.ToString("o"), // ISO 8601 format
            ["x-sagaway-dapr-custom-metadata"] = customMetadata
        };

        LogDebugContext("capture callback context", callbackContext);
        
        // Return the serialized JSON string
        return callbackContext;
    }


    /// <summary>
    /// Invokes a method on a sub-saga actor using an expression to capture the method call.
    /// Extracts the method name and parameters from the provided expression and sends them via Dapr binding.
    /// Includes support for custom callback contexts, metadata, and binding options.
    /// </summary>
    /// <typeparam name="TSubSaga">The type of the interface of the sub-saga actor.</typeparam>
    /// <param name="methodExpression">An expression that represents the method to invoke on the sub-saga actor.</param>
    /// <param name="actorTypeName">The type name of the actor as registered in the Dapr actor runtime.</param>
    /// <param name="newActorId">The unique identifier of the sub-saga actor.</param>
    /// <param name="options">
    /// A set of additional options for the sub-saga call, including:
    /// <list type="bullet">
    /// <item><description>CallbackMethodName: The method to invoke in the main saga after the sub-saga completes.</description></item>
    /// <item><description>CustomSagawayMetadata: Metadata to include in the Dapr binding call context.</description></item>
    /// <item><description>CustomBindingMetadata: A dictionary of additional binding metadata, which will override default values if keys overlap.</description></item>
    /// <item><description>UseBindingName: Specifies an alternate binding name, defaulting to the callback binding name if not set.</description></item>
    /// </list>
    /// </param>
    /// <returns>A task representing the asynchronous operation of invoking the sub-saga actor.</returns>
    /// <example>
    /// Example usage:
    /// <code>
    /// await CallSubSagaAsync&lt;ISubSaga&gt;(
    ///     saga => saga.ExecuteAsync(param1, param2),
    ///     "SubSagaActor",
    ///     "SubSaga123",
    ///     new CallSubSagaOptions(
    ///         CallbackMethodName: "MainSagaCallback",
    ///         CustomSagawayMetadata: "SomeMetadata",
    ///         CustomBindingMetadata: new Dictionary&lt;string, string&gt;
    ///         {
    ///             { "key1", "value1" },
    ///             { "key2", "value2" }
    ///         },
    ///         UseBindingName: "customBindingName"));
    /// </code>
    /// </example>
    protected async Task CallSubSagaAsync<TSubSaga>(Expression<Func<TSubSaga, Task>> methodExpression, string actorTypeName, 
        string newActorId, CallSubSagaOptions options)
        where TSubSaga : ISagawayActor
    {
        _logger.LogInformation("Starting sub-saga with actor id {NewActorId} using method {CallbackMethodName}", newActorId, options.CallbackMethodName);
        
        // Use the method name of StartSubSagaWithContextAsync to handle the sub-saga dispatch
        var callbackContext = CaptureCallbackContext(options.CallbackMethodName);

        // Extract method name and parameters from the expression
        var methodCall = (MethodCallExpression)methodExpression.Body;
        var methodName = methodCall.Method.Name;
        var arguments = methodCall.Arguments.Select(a => Expression.Lambda(a).Compile().DynamicInvoke()).ToArray();

        _logger.LogDebug("Extracted method {MethodName} with arguments for sub-saga", methodName);

        // Prepare the SubSagaInvocationContext object
        var invocationContext = new SubSagaInvocationContext
        {
            MethodName = methodName,  // The target method to invoke in the sub-saga
            CallbackContext = callbackContext,
            ParametersJson = JsonSerializer.Serialize(arguments, GetJsonSerializerOptions())
        };

        var invokeDispatcherParameters = new Dictionary<string, string>()
        {
            ["x-sagaway-dapr-callback-method-name"] = nameof(ProcessASubSagaCallAsync),
            ["x-sagaway-dapr-actor-id"] = newActorId,
            ["x-sagaway-dapr-actor-type"] = actorTypeName,
            ["x-sagaway-dapr-message-dispatch-time"] = DateTime.UtcNow.ToString("o"), // ISO 8601 format
            ["x-sagaway-dapr-custom-metadata"] = options.CustomSagawayMetadata,
        };

        LogDebugContext("Sub Saga call context", invokeDispatcherParameters);

        if (options.CustomBindingMetadata is not null)
        {
            foreach (var (key, value) in options.CustomBindingMetadata)
            {
                invokeDispatcherParameters[key] = value;
            }
        }

        _logger.LogInformation("Dispatching sub-saga invocation for method {MethodName}", methodName);

        // Create a new DaprClient for the sub-saga invocation, so it will not use the preconfigured HttpClient with the default headers
        var daprClientBuilder = new DaprClientBuilder();
        var subSagaDaprClient = daprClientBuilder.Build(); // No custom headers for sub-saga

        var bindingName = string.IsNullOrWhiteSpace(options.UseBindingName) 
            ? GetCallbackBindingName() 
            : options.UseBindingName;

        // Dispatch the sub-saga invocation with a single parameter (invocationContext)
        await subSagaDaprClient.InvokeBindingAsync(
            bindingName, 
            "create",  // Binding operation
            invocationContext,
            invokeDispatcherParameters
        );

        _logger.LogInformation("Sub-saga invocation dispatched successfully for method {MethodName}", methodName);
    }

    /// <summary>
    /// Call or start a sub-saga by invoking a method on a sub-saga actor using an expression to capture the method call.
    /// The method extracts the function name and parameters from the provided expression, and sends them via Dapr binding,
    /// along with the captured callback context to enable communication between the main and sub-sagas.
    /// </summary>
    /// <typeparam name="TSubSaga">The type of the interface of the sub-saga actor.</typeparam>
    /// <param name="methodExpression">An expression that represents the method to call on the sub-saga.</param>
    /// <param name="actorTypeName">The type name of the actor as added to the Dapr actor runtime</param>
    /// <param name="newActorId">The identity of the sub-saga actor</param>
    /// <param name="callbackMethodName">The name of the method in the main saga to call back once the sub-saga finishes its operation.</param>
    /// <param name="customMetadata">Any additional metadata that can be flow with the call context</param>
    /// <returns>A task representing the asynchronous operation of invoking the sub-saga.</returns>
    /// <example>
    /// Example usage:
    /// <code>
    /// await StartSubSagaAsync&lt;ISubSaga&gt;(s => s.DoSomethingAsync(param1, param2), "MainSagaCallback");
    /// </code>
    /// This will invoke the "DoSomethingAsync" method on the sub-saga and allow for a callback to the "MainSagaCallback" method on completion.
    /// </example>
    [Obsolete("Use overload with options instead")]
    protected async Task CallSubSagaAsync<TSubSaga>(Expression<Func<TSubSaga, Task>> methodExpression, string actorTypeName,
        string newActorId, string callbackMethodName = "", string customMetadata = "")
        where TSubSaga : ISagawayActor
    {
        _logger.LogInformation("Starting sub-saga with actor id {NewActorId} using method {CallbackMethodName}", newActorId, callbackMethodName);

        // Use the method name of StartSubSagaWithContextAsync to handle the sub-saga dispatch
        var callbackContext = CaptureCallbackContext(callbackMethodName);

        // Extract method name and parameters from the expression
        var methodCall = (MethodCallExpression)methodExpression.Body;
        var methodName = methodCall.Method.Name;
        var arguments = methodCall.Arguments.Select(a => Expression.Lambda(a).Compile().DynamicInvoke()).ToArray();

        _logger.LogDebug("Extracted method {MethodName} with arguments for sub-saga", methodName);

        // Prepare the SubSagaInvocationContext object
        var invocationContext = new SubSagaInvocationContext
        {
            MethodName = methodName,  // The target method to invoke in the sub-saga
            CallbackContext = callbackContext,
            ParametersJson = JsonSerializer.Serialize(arguments, GetJsonSerializerOptions())
        };

        var invokeDispatcherParameters = new Dictionary<string, string>()
        {
            ["x-sagaway-dapr-callback-method-name"] = nameof(ProcessASubSagaCallAsync),
            ["x-sagaway-dapr-actor-id"] = newActorId,
            ["x-sagaway-dapr-actor-type"] = actorTypeName,
            ["x-sagaway-dapr-message-dispatch-time"] = DateTime.UtcNow.ToString("o"), // ISO 8601 format
            ["x-sagaway-dapr-custom-metadata"] = customMetadata
        };

        LogDebugContext("Sub Saga call context", invokeDispatcherParameters);

        _logger.LogInformation("Dispatching sub-saga invocation for method {MethodName}", methodName);

        // Create a new DaprClient for the sub-saga invocation, so it will not use the preconfigured HttpClient with the default headers
        var daprClientBuilder = new DaprClientBuilder();
        var subSagaDaprClient = daprClientBuilder.Build(); // No custom headers for sub-saga

        // Dispatch the sub-saga invocation with a single parameter (invocationContext)
        await subSagaDaprClient.InvokeBindingAsync(
            GetCallbackBindingName(),
            "create",  // Binding operation
            invocationContext,
            invokeDispatcherParameters
        );

        _logger.LogInformation("Sub-saga invocation dispatched successfully for method {MethodName}", methodName);
    }

    public async Task ProcessASubSagaCallAsync(SubSagaInvocationContext context)
    {
        _logger.LogDebug("SubSagaStartAsync invoked with method {MethodName}", context.MethodName);

        // Store the callback context in the actor's state
        var mainSagaCallbackContext =
            new JsonObject(context.CallbackContext.ToDictionary(kvp => kvp.Key, kvp => (JsonNode)kvp.Value)!);
        await StateManager.SetStateAsync("MainSagaCallbackContext", mainSagaCallbackContext);

        _logger.LogInformation("Stored MainSagaCallbackContext in actor state.");

        // Deserialize the parameters from JSON
        var parameterElements = JsonSerializer.Deserialize<JsonElement[]>(context.ParametersJson);
        _logger.LogDebug("Deserialized parameters for method {MethodName}", context.MethodName);

        // Use reflection to invoke the actual method on the sub-saga
        MethodInfo? methodInfo = GetType().GetMethod(context.MethodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (methodInfo == null)
        {
            _logger.LogCritical("Method {MethodName} not found in actor.", context.MethodName);
            throw new InvalidOperationException($"Method {context.MethodName} not found in actor.");
        }

        // Get the parameter types for the target method
        var parameterTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();

        // Handle case where parameters are null
        object[] parameters;
        if (parameterElements == null)
        {
            if (parameterTypes.Length > 0)
            {
                _logger.LogCritical("Method {MethodName} expects parameters, but none were provided.", context.MethodName);
                throw new InvalidOperationException($"Method {context.MethodName} expects parameters, but none were provided.");
            }
            parameters = []; // No parameters required
        }
        else
        {
            // Convert the JSON parameters to the required types using dynamic deserialization
            parameters = new object[parameterElements.Length];
            for (int i = 0; i < parameterElements.Length; i++)
            {
                parameters[i] = ConvertJsonElementToType(parameterElements[i], parameterTypes[i]);
            }
        }

        _logger.LogInformation("Invoking method {MethodName} on sub-saga.", context.MethodName);

        // Call the target method with the converted parameters
        var result = methodInfo.Invoke(this, parameters);

        // If the method is asynchronous (returns a Task), await it
        if (result is Task task)
        {
            await task;
        }
    }

    private object ConvertJsonElementToType(JsonElement jsonElement, Type targetType)
    {
        // Use JsonSerializer to deserialize dynamically based on targetType
        return JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType, GetJsonSerializerOptions())
               ?? throw new InvalidOperationException($"Failed to deserialize {jsonElement} to {targetType.Name}");
    }

    /// <summary>
    /// Calls back the main saga with a result after the sub-saga completes its operation.
    /// This method retrieves the stored callback context for the main saga and sends the result back to it.
    /// </summary>
    /// <typeparam name="T">The type of the result that will be sent back to the main saga.</typeparam>
    /// <param name="result">The result object to send back to the main saga.</param>
    /// <returns>A task representing the asynchronous operation of invoking the main saga callback.</returns>
    /// <example>
    /// Example usage in a sub-saga:
    /// <code>
    /// // Assume some result needs to be sent back to the main saga
    /// var result = new MyResult { Value = "some value" };
    /// await CallbackMainSagaAsync(result);
    /// </code>
    ///
    /// Example usage in the main saga:
    /// <code>
    /// public async Task OnSubSagaCallbackAsync(MyResult result)
    /// {
    ///     // Handle the result from the sub-saga
    ///     Console.WriteLine($"Received result: {result.Value}");
    /// }
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the callback context is not found in the state store.
    /// </exception>
    protected async Task CallbackMainSagaAsync<T>(T result)
    {
        _logger.LogDebug("CallbackMainSagaAsync invoked with result of type {Type}", typeof(T).Name);

        // Retrieve the callback context from the state store
        var callbackContext = await StateManager.GetStateAsync<JsonObject>("MainSagaCallbackContext");
        if (callbackContext == null || string.IsNullOrEmpty(callbackContext.Root["x-sagaway-dapr-callback-method-name"]?.GetValue<string>()))
        {
            _logger.LogCritical("Main saga callback context not found in actor state. Forgot to pass it?");
            throw new InvalidOperationException("Main saga callback context not found. Forgot to pass it?");
        }

        _logger.LogInformation("Retrieved callback context for MainSagaCallback.");

        // Use the callback context to get the metadata for the binding
        var callbackMetadata = callbackContext.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value!.ToString() ?? throw new InvalidOperationException($"Invalid value for {kvp.Key}")
        );

        // Create a new DaprClient for the sub-saga invocation
        var daprClientBuilder = new DaprClientBuilder();
        var daprClient = daprClientBuilder.Build(); // No custom headers for sub-saga
        var callbackBinding = GetCallbackBindingName() ?? throw new InvalidOperationException("Callback binding name not found in callback context.");

        // Call the main saga via Dapr binding with the result and callback metadata
        await daprClient.InvokeBindingAsync(
            callbackBinding,
            "create", // or another operation
            result,  // Send the payload with the object directly
            callbackMetadata
        );

        _logger.LogInformation("Successfully invoked MainSagaCallback with the result.");
    }

    private void LogDebugContext(string message, IDictionary<string, string> context)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            foreach (var kvp in context)
            {
                sb.AppendLine($" {kvp.Key}={kvp.Value}");
            }

            _logger.LogDebug("{message}, context: {context}",
                message, sb.ToString());
        }
    }
}