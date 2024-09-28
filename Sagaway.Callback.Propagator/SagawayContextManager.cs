using System.Text;
using System.Text.Json;
using System.Web;

namespace Sagaway.Callback.Propagator;

/// <summary>
/// Manages the Sagaway context, providing methods to retrieve and apply the context across distributed services.
/// </summary>
public class SagawayContextManager : ISagawayContextManager
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Gets the current Sagaway context from AsyncLocal as a Base64-encoded serialized string.
    /// </summary>
    /// <returns>The current Sagaway context as a Base64-encoded serialized string.</returns>
    public string Context
    {
        get
        {
            if (HeaderPropagationMiddleware.SagawayContext.Value == null)
                throw new InvalidOperationException("There is no sagaway context to capture");

            var context = ConvertToBase64(HeaderPropagationMiddleware.SagawayContext.Value);

            return context;
        }
    }

    /// <summary>
    /// Gets the current Sagaway context with the custom metadata property contains the custom metadata
    /// as a base64 serialized string.
    /// This context can be used to propagate across service boundaries.
    /// </summary>
    /// <param name="customMetadata">The custom metadata to include in the context.</param>
    /// <returns>The current Sagaway context with the custom metadata as a base64 serialized string.</returns>
    public string GetContextWithMetadata(string customMetadata = "")
    {
        var context = (HeaderPropagationMiddleware.SagawayContext.Value ?? new SagawayContext())
            with
        {
            Metadata = customMetadata
        };

        var result = ConvertToBase64(context);

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
            var context = ConvertFromBase64<SagawayContext>(sagaContext);

            // Store the deserialized context in AsyncLocal via HeaderPropagationMiddleware
            HeaderPropagationMiddleware.SagawayContext.Value = context;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to apply the context", ex);
        }
    }

    /// <summary>
    /// Gets the headers from the provided Sagaway context.
    /// If the context is null, the current Sagaway call context is used.
    /// </summary>
    /// <param name="context">The Sagaway context.</param>
    /// <returns>A dictionary of headers extracted from the Sagaway context.</returns>
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

    /// <summary>
    /// Gets the Sagaway context from the provided headers.
    /// </summary>
    /// <param name="headers">The headers containing the Sagaway context information.</param>
    /// <returns>The Sagaway context extracted from the headers.</returns>
    public SagawayContext GetSagawayContextFromHeaders(Dictionary<string, string?> headers)
    {
        if (headers == null)
            throw new ArgumentNullException(nameof(headers));

        headers.TryGetValue("x-sagaway-dapr-actor-id", out var actorId);

        headers.TryGetValue("x-sagaway-dapr-actor-type", out var actorType);

        headers.TryGetValue("x-sagaway-dapr-callback-binding-name", out var callbackBindingName);

        headers.TryGetValue("x-sagaway-dapr-callback-method-name", out var callbackMethodName);

        headers.TryGetValue("x-sagaway-dapr-message-dispatch-time", out var messageDispatchTime);

        headers.TryGetValue("x-sagaway-dapr-custom-metadata", out var customMetadata);

        return new SagawayContext(actorId, actorType, callbackBindingName, callbackMethodName, messageDispatchTime, customMetadata);
    }

    /// <summary>
    /// Return the Sagaway context from the provided base64 string.
    /// </summary>
    /// <param name="base64String">The json string of the context converted to Base64</param>
    /// <returns>The Sagaway call context</returns>
    // ReSharper disable once UnusedMember.Global
    public SagawayContext GetSagawayContextFromBase64String(string base64String)
    {
        return ConvertFromBase64<SagawayContext>(base64String);
    }

    public T ConvertFromBase64<T>(string base64String)
    {
        var decodedBase64 = HttpUtility.UrlDecode(base64String);
        var jsonString = Encoding.UTF8.GetString(Convert.FromBase64String(decodedBase64));

        var context = JsonSerializer.Deserialize<T>(jsonString, _jsonSerializerOptions) ??
                      throw new InvalidOperationException("Can't deserialize base64String json content");

        return context;
    }

    public string ConvertToBase64<T>(T instance)
    {
        var jsonString = JsonSerializer.Serialize(instance, _jsonSerializerOptions);
        var base64String = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonString));
        return base64String;
    }
}
