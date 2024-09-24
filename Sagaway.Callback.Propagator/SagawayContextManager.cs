using System.Text;
using System.Text.Json;

namespace Sagaway.Callback.Propagator;

public class SagawayContextManager : ISagawayContextManager
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Gets the current Sagaway context from AsyncLocal as a Base64-encoded serialized string.
    /// </summary>
    public string Context
    {
        get
        {
            if (HeaderPropagationMiddleware.SagawayContext.Value == null)
                throw new InvalidOperationException("There is no sagaway context to capture");

            var context = ConvertContextToBase64(HeaderPropagationMiddleware.SagawayContext.Value);

            return context;
        }
    }

    /// <summary>
    /// Gets the current Sagaway context with the custom metadata property contains the custom metadata
    /// as a base64 serialized string.
    /// This context can be used to propagate across service boundaries.
    /// </summary>
    /// <remarks>As opposed the <see cref="Context"> if there is no active Sagaway context, a default header
    /// with the provided metadata is returned</see></remarks>
    public string GetContextWithMetadata(string customMetadata = "")
    {
        var context = (HeaderPropagationMiddleware.SagawayContext.Value ?? new SagawayContext())
            with
            {
                Metadata = customMetadata
            };


        var result = ConvertContextToBase64(context);

        return result;
    }

    /// <summary>
    /// Applies a given Base64-encoded serialized saga context to the AsyncLocal context.
    /// </summary>
    /// <param name="sagaContext">The Base64-encoded serialized context string to be applied.</param>
    public void ApplyContext(string sagaContext)
    {
        if (string.IsNullOrEmpty(sagaContext)) 
            throw new InvalidOperationException($"{nameof(sagaContext)} is null or empty");

        try
        {
            var jsonString = Encoding.UTF8.GetString(Convert.FromBase64String(sagaContext));
            var context = JsonSerializer.Deserialize<SagawayContext>(jsonString, _jsonSerializerOptions) ?? 
                          throw new InvalidOperationException("Can't deserialize Sagaway context");

            // Store the deserialized context in AsyncLocal via HeaderPropagationMiddleware
            HeaderPropagationMiddleware.SagawayContext.Value = context;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to apply the context", ex);
        }
    }

    public Dictionary<string, string> GetHeaders(SagawayContext? context)
    {
        context ??= HeaderPropagationMiddleware.SagawayContext.Value;

        if (context is null)
            throw new InvalidOperationException("There is no sagaway context to capture");

        return new Dictionary<string, string>
        {
            ["x-sagaway-dapr-actor-id"] = context.ActorId ?? string.Empty,
            ["x-sagaway-dapr-actor-type"] = context.ActorType ?? string.Empty,
            ["x-sagaway-dapr-callback-binding-name"] = context.CallbackBindingName ?? string.Empty,
            ["x-sagaway-dapr-callback-method-name"] = context.CallbackMethodName ?? string.Empty,
            ["x-sagaway-dapr-message-dispatch-time"] = context.MessageDispatchTime ?? string.Empty,
            ["x-sagaway-dapr-custom-metadata"] = context.Metadata ?? string.Empty
        };
    }

    private string ConvertContextToBase64(SagawayContext context)
    {
        var jsonString = JsonSerializer.Serialize(context, _jsonSerializerOptions);
        var base64String = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonString));
        return base64String;
    }
}