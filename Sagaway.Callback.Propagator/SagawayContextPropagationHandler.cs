namespace Sagaway.Callback.Propagator;

public class SagawayContextPropagationHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        //propagate actor id
        if (!string.IsNullOrEmpty(HeaderPropagationMiddleware.ActorId.Value))
            request.Headers.TryAddWithoutValidation("x-sagaway-dapr-actor-id", [HeaderPropagationMiddleware.ActorId.Value]);
        
        //propagate callback method name
        if (!string.IsNullOrEmpty(HeaderPropagationMiddleware.CallbackMethodName.Value))
            request.Headers.TryAddWithoutValidation("x-sagaway-callback-method", [HeaderPropagationMiddleware.CallbackMethodName.Value]);

        return await base.SendAsync(request, cancellationToken);
    }
}