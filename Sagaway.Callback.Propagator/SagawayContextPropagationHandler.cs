namespace Sagaway.Callback.Propagator;

public class SagawayContextPropagationHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        //propagate actor id
        if (!string.IsNullOrEmpty(HeaderPropagationMiddleware.SagawayContext.Value?.ActorId))
            request.Headers.TryAddWithoutValidation("x-sagaway-dapr-actor-id", [HeaderPropagationMiddleware.SagawayContext.Value.ActorId]);
        
        //propagate actor type
        if (!string.IsNullOrEmpty(HeaderPropagationMiddleware.SagawayContext.Value?.ActorType))
            request.Headers.TryAddWithoutValidation("x-sagaway-dapr-actor-type", [HeaderPropagationMiddleware.SagawayContext.Value.ActorType]);

        //propagate callback method name
        if (!string.IsNullOrEmpty(HeaderPropagationMiddleware.SagawayContext.Value?.CallbackMethodName))
            request.Headers.TryAddWithoutValidation("x-sagaway-dapr-callback-method-name", [HeaderPropagationMiddleware.SagawayContext.Value.CallbackMethodName]);
        
        return await base.SendAsync(request, cancellationToken);
    }
}