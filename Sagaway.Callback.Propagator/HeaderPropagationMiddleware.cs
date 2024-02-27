namespace Sagaway.Callback.Propagator;

using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

public class HeaderPropagationMiddleware(RequestDelegate next)
{
    // Using AsyncLocal to store the headers per request context
    public static readonly AsyncLocal<string?> ActorId = new();
    public static readonly AsyncLocal<string?> CallbackQueueName = new();
    public static readonly AsyncLocal<string?> CallbackMethodName = new();
    public static readonly AsyncLocal<string?> MessageDispatchTime = new();

    public async Task InvokeAsync(HttpContext context)
    {
        ActorId.Value = context.Request.Headers["x-sagaway-dapr-actor-id"];
        CallbackQueueName.Value = context.Request.Headers["x-sagaway-callback-queue-name"];
        CallbackMethodName.Value = context.Request.Headers["x-sagaway-callback-method"];
        
        // Call the next delegate/middleware in the pipeline
        await next(context);
    }
}