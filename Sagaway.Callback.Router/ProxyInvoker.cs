using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Polly;
using Sagaway.Callback.Context;

namespace Sagaway.Callback.Router;

/// <summary>
/// ProxyInvoker is responsible for intercepting calls, extracting Sagaway context, and dispatching 
/// callbacks to the appropriate service or actor.
/// </summary>
/// <param name="logger">The logger used for logging errors, warnings, and informational messages.</param>
/// <param name="sagawayContextManager">Manages the current Sagaway context and provides access to context information.</param>
/// <param name="sagawayCallerIdProvider">Provides the caller ID for comparison with the Sagaway context.</param>
/// <param name="sagawayCallbackInvoker">Used to dispatch callbacks to the appropriate service or actor.</param>
internal class ProxyInvoker(
    ILogger logger, 
    ISagawayContextManager sagawayContextManager,
    ISagawayCallerIdProvider sagawayCallerIdProvider,
    ISagawayCallbackInvoker sagawayCallbackInvoker)
{
    /// <summary>
    /// Invokes the target method by extracting the Sagaway context, validating it, and dispatching the callback.
    /// </summary>
    /// <param name="payload">The JSON payload to be sent in the callback.</param>
    /// <param name="nextActionAsync">The delegate to call the next handler in the chain if the context is invalid.</param>
    /// <param name="headers">The HTTP headers containing Sagaway context information.</param>
    /// <returns>An awaitable task that returns the result of the invocation or passes control to the next handler.</returns>
    public async ValueTask<object?> InvokeAsync(JsonNode? payload, 
        Func<Task<object?>> nextActionAsync, IHeaderDictionary headers)
    {

        if (payload is null)
        {
            logger.LogInformation("SagawayCallbackFilter: Payload is null or empty, ignoring call");
            return await nextActionAsync();
        }

        if (!headers.TryGetValue(sagawayContextManager.SagaWayContextHeaderKeyName, out var
                contextHeaderValue))
        {
            logger.LogInformation("SagawayCallbackFilter: no sagaway context found, ignoring call");
            return await nextActionAsync();
        }
        //else

        var sagawayCallContext = contextHeaderValue.FirstOrDefault();
        if (string.IsNullOrEmpty(sagawayCallContext))
        {
            logger.LogError("Sagaway context header found but was empty or null.");
            return await nextActionAsync();
        }

        //else
        logger.LogInformation("Propagating context from header: {SagaWayContextHeader}", sagawayCallContext);
        sagawayContextManager.SetContextFromIncomingRequest(sagawayCallContext);
    

        var sagawayContext = sagawayContextManager.GetCallerContext();

        if (sagawayContext is null)
        {
            logger.LogInformation("SagawayCallbackFilter: context is null or empty, ignoring the call");
            return await nextActionAsync();
        }

        if (string.IsNullOrEmpty(sagawayContext.CallerId))
        {
            logger.LogError("SagawayCallbackFilter: CallerId is null or empty, ignoring the call");
        }

        if (sagawayContext.CallerId != sagawayCallerIdProvider.CallerId)
        {
            logger.LogError("SagawayCallbackFilter: CallerId (ActorId) does not match, ignoring the call");
            return await nextActionAsync();
        }

        if (string.IsNullOrEmpty(sagawayContext.CallbackMethodName))
        {
            logger.LogError("Callback method name is null or empty, ignoring the call");
            return await nextActionAsync();
        }

        if (string.IsNullOrEmpty(sagawayContext.CallerType))
        {
            logger.LogError("Caller type is null or empty, ignoring the call");
            return await nextActionAsync();
        }

        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(4, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Min(Math.Pow(1, retryAttempt), 30)) +
                    TimeSpan.FromMilliseconds(new Random().Next(0, 1000)),
                (exception, timeSpan, retryCount, _) =>
                {
                    logger.LogWarning(exception, "Retry {RetryCount} for method {MethodName} on  {CallerType} failed. Retrying in {Delay} seconds...",
                        retryCount, sagawayContext.CallbackMethodName, sagawayContext.CallerType, timeSpan.TotalSeconds);
                });

        try
        {
            logger.LogInformation("Dispatched callback to {CallerType} for method {MethodName}",
                sagawayContext.CallerType, sagawayContext.CallbackMethodName);

            await retryPolicy.ExecuteAsync(async () =>
            {
                await sagawayCallbackInvoker.DispatchCallbackAsync(payload.ToJsonString(), sagawayContext);
            });

            return Results.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error dispatching callback to {ActorTypeName} for method {MethodName} with Actor ID {ActorId}", sagawayContext.CallerType, sagawayContext.CallbackMethodName, sagawayContext.CallerId);
            return Results.Ok();
        }
    }
}