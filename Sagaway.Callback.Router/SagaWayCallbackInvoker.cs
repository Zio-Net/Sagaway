using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sagaway.Callback.Context;

namespace Sagaway.Callback.Router;

/// <summary>
/// Implements the <see cref="ISagawayCallbackInvoker"/> to handle the dispatching of callbacks either to a service
/// or to an actor based on the Sagaway context. This implementation dynamically determines whether to dispatch to
/// an actor or a regular service, using reflection to avoid direct dependencies.
/// </summary>
/// <param name="logger">The logger to log messages during callback dispatch.</param>
/// <param name="serviceProvider">The service provider to resolve services and actors dynamically.</param>
public class SagaWayCallbackInvoker(
    ILogger<SagaWayCallbackInvoker> logger,
    IServiceProvider serviceProvider) : ISagawayCallbackInvoker
{
    private readonly JsonSerializerOptions _jsonSerializationOptions = 
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    /// <summary>
    /// Dispatches the callback based on the Sagaway context. It determines whether the callback is for an actor
    /// or a service and dispatches accordingly.
    /// </summary>
    /// <param name="payloadJson">The JSON payload for the callback method.</param>
    /// <param name="sagawayContext">The Sagaway context containing routing and metadata information.</param>
    public async Task DispatchCallbackAsync(string payloadJson, SagawayContext sagawayContext)
    {
        if (!string.IsNullOrEmpty(sagawayContext.CallerId))
        {
            await DispatchCallbackActorAsync(payloadJson, sagawayContext);
            return;
        }
        //else
        await DispatchCallbackServiceAsync(payloadJson, sagawayContext);
    }

    /// <summary>
    /// Dispatches the callback to a registered service using reflection to resolve and invoke the appropriate method.
    /// </summary>
    /// <param name="payloadJson">The JSON payload to be passed to the method.</param>
    /// <param name="sagawayContext">The Sagaway context containing method and service information.</param>
    private async Task DispatchCallbackServiceAsync(string payloadJson, SagawayContext sagawayContext)
    {
        try
        {
            var serviceType = Type.GetType(sagawayContext.CallerType!);
            if (serviceType == null)
            {
                logger.LogError("Service type {CallerType} not found.", sagawayContext.CallerType);
                return;
            }

            var serviceInstance = serviceProvider.GetService(serviceType);
            if (serviceInstance == null)
            {
                logger.LogError("Service {CallerType} could not be resolved from DI container.",
                    sagawayContext.CallerType);
                return;
            }

            // Use reflection to invoke the method
            var methodInfo = serviceType.GetMethod(sagawayContext.CallbackMethodName!);
            if (methodInfo == null)
            {
                logger.LogError("Method {MethodName} not found in service {CallerType}.",
                    sagawayContext.CallbackMethodName, sagawayContext.CallerType);
                return;
            }

            var parameters = methodInfo.GetParameters();
            if (parameters.Length != 1)
            {
                logger.LogError("Method {CallbackMethodName} does not accept exactly one parameter.",
                    sagawayContext.CallbackMethodName);

                throw new InvalidOperationException(
                    $"Method {sagawayContext.CallbackMethodName} signature mismatch: expected exactly one parameter.");
            }

            var parameterType = parameters.First().ParameterType;

            // Deserialize the payload to the expected parameter type
            var parameter = JsonSerializer.Deserialize(payloadJson, parameterType, _jsonSerializationOptions);
            if (parameter == null)
            {
                logger.LogError(
                    "Unable to deserialize payload to type {parameterType.Name} for method {methodName}.",
                    parameterType.Name, sagawayContext.CallbackMethodName);
                throw new InvalidOperationException(
                    $"Payload deserialization failed for method {sagawayContext.CallbackMethodName}.");
            }

            //no need for retries, the callback is already retried by the caller
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
            logger.LogError(tie, "Error invoking method {sagawayContext.CallerType}.{sagawayContext.CallbackMethodName}.",
                sagawayContext.CallerType,  sagawayContext.CallbackMethodName);
            throw;
        }
    }

    /// <summary>
    /// Dispatches the callback to a Dapr actor if the Actor system is available, using reflection to resolve
    /// the actor proxy and invoke the method.
    /// </summary>
    /// <param name="payloadJson">The JSON payload to pass to the actor method.</param>
    /// <param name="sagawayContext">The Sagaway context containing actor information (CallerId and CallerType).</param>
    private async Task DispatchCallbackActorAsync(string payloadJson, SagawayContext sagawayContext)
    {
        try
        {
            // Dynamically check if IActorProxyFactory exists and create actor proxy if available
            var actorProxyFactoryType = Type.GetType("Dapr.Actors.Client.IActorProxyFactory, Dapr.Actors");
            if (actorProxyFactoryType == null)
            {
                logger.LogError("Dapr Actor system is not available. Cannot dispatch callback to Actor.");
                throw new InvalidOperationException("Dapr Actor system is not available.");
            }
            //else

            logger.LogInformation("Actor system is available. Dispatching callback to Actor.");

            var actorProxyFactory = serviceProvider.GetService(actorProxyFactoryType);
            if (actorProxyFactory == null)
            {
                logger.LogError("Actor proxy factory could not be resolved from DI container.");

                throw new InvalidOperationException("Actor proxy factory could not be resolved.");
            }

            //else

            var actorIdType = Type.GetType("Dapr.Actors.ActorId, Dapr.Actors");
            var actorId = Activator.CreateInstance(actorIdType!, sagawayContext.CallerId);

            var createActorProxyMethod = actorProxyFactoryType.GetMethod("CreateActorProxy")
                ?.MakeGenericMethod(Type.GetType(sagawayContext.CallerType!)!);

            var actorProxy = createActorProxyMethod?.Invoke(actorProxyFactory,
                [actorId, sagawayContext.CallerType]);

            if (actorProxy == null)
            {
                logger.LogError("Actor proxy could not be created for Actor ID {ActorId} and Actor Type {ActorType}.",
                    sagawayContext.CallerId, sagawayContext.CallerType);
                throw new InvalidOperationException("Actor proxy could not be created.");
            }

            var dispatchCallbackMethod = actorProxy.GetType().GetMethod("DispatchCallbackAsync");
            if (dispatchCallbackMethod == null)
            {
                logger.LogError("DispatchCallbackAsync method not found in Actor {ActorType}.",
                    sagawayContext.CallerType);
            }

            await ((Task)dispatchCallbackMethod!.Invoke(actorProxy,
                [payloadJson, sagawayContext.CallbackMethodName!])!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error dispatching callback to {CallerType}.{CallbackMethodName}",
                sagawayContext.CallerType, sagawayContext.CallbackMethodName);
            throw;
        }
    }
}
