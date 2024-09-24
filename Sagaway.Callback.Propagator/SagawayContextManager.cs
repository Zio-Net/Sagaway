using System.Text;
using System.Text.Json;

namespace Sagaway.Callback.Propagator;

public class SagawayContextManager : ISagawayContextManager
{
    /// <summary>
    /// Gets the current Sagaway context from AsyncLocal as a Base64-encoded serialized string.
    /// </summary>
    public string SagawayContext
    {
        get
        {
            if (HeaderPropagationMiddleware.SagawayContext.Value == null)
                throw new InvalidOperationException("There is no sagaway context to capture");

            var jsonString = JsonSerializer.Serialize(HeaderPropagationMiddleware.SagawayContext.Value);
            var base64String = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonString));
            return base64String;
        }
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
            var context = JsonSerializer.Deserialize<SagawayContext>(jsonString) ?? 
                          throw new InvalidOperationException("Can't deserialize Sagaway context");

            // Store the deserialized context in AsyncLocal via HeaderPropagationMiddleware
            HeaderPropagationMiddleware.SagawayContext.Value = context;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to apply the context", ex);
        }

    }
}