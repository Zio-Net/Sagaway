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

        //propagate message callback binding name
        if (!string.IsNullOrEmpty(HeaderPropagationMiddleware.SagawayContext.Value?.CallbackBindingName))
            request.Headers.TryAddWithoutValidation("x-sagaway-dapr-callback-binding-name", [HeaderPropagationMiddleware.SagawayContext.Value.CallbackBindingName]);

        //propagate message dispatch time
        if (!string.IsNullOrEmpty(HeaderPropagationMiddleware.SagawayContext.Value?.MessageDispatchTime))
            request.Headers.TryAddWithoutValidation("x-sagaway-dapr-message-dispatch-time", [HeaderPropagationMiddleware.SagawayContext.Value.MessageDispatchTime]);

        //propagate custom metadata
        if (!string.IsNullOrEmpty(HeaderPropagationMiddleware.SagawayContext.Value?.Metadata))
            request.Headers.TryAddWithoutValidation("x-sagaway-dapr-custom-metadata", [HeaderPropagationMiddleware.SagawayContext.Value.Metadata]);

        return await base.SendAsync(request, cancellationToken);
    }
}