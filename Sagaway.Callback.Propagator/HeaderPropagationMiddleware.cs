using Microsoft.Extensions.Logging;
using Sagaway.Callback.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Sagaway.Callback.Propagator;

/// <summary>
/// Middleware for propagating the Sagaway context from incoming HTTP request headers.
/// This middleware extracts the context from the request and sets it in the SagawayContextManager.
/// </summary>
public class HeaderPropagationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HeaderPropagationMiddleware> _logger;
    
    // ReSharper disable once ConvertToPrimaryConstructor
    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderPropagationMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger instance for logging information and warnings.</param>
    /// <exception cref="ArgumentNullException">Thrown if any of the parameters are null.</exception>
    public HeaderPropagationMiddleware(RequestDelegate next,
        ILogger<HeaderPropagationMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the callback binding name from the current Sagaway context.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string? CallbackBindingName { private set; get; }

    /// <summary>
    /// Invokes the middleware to propagate the Sagaway context from the incoming request headers.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the context is null.</exception>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Resolve the ISagawayContextManager within the request scope
        var sagawayContextManager = context.RequestServices.GetRequiredService<ISagawayContextManager>();

        try
        {
            CallbackBindingName = sagawayContextManager.SagaWayContextHeaderKeyName;

            if (!context.Request.Headers.TryGetValue(sagawayContextManager.SagaWayContextHeaderKeyName,
                    out var sagawayCallContextValues))
            {
                _logger.LogDebug("No Sagaway context header found in the request.");
                return;
            }
            
            var sagawayCallContext = sagawayCallContextValues.FirstOrDefault();

            // Check if the value of the first header entry is not null or empty
            if (string.IsNullOrEmpty(sagawayCallContext))
            {
                _logger.LogWarning("Sagaway context header found but was empty or null.");
                return;
            }
            
            _logger.LogInformation("Propagating context from header: {SagaWayContextHeader}", sagawayCallContext);
            sagawayContextManager.SetContextFromIncomingRequest(sagawayCallContext);
        }
        finally
        {
            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
