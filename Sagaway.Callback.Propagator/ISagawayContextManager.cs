using Microsoft.AspNetCore.Http;

namespace Sagaway.Callback.Propagator;

/// <summary>
/// Defines the contract for managing the Sagaway context, providing methods
/// to retrieve and apply the context across distributed services.
/// </summary>
public interface ISagawayContextManager
{
    /// <summary>
    /// Gets the current Sagaway context as a base64 serialized string. 
    /// This context can be used to propagate across service boundaries.
    /// </summary>
    /// <returns>The current Sagaway context as a base64 serialized string.</returns>
    string Context { get; }

    /// <summary>
    /// Gets the current Sagaway context with the custom metadata property contains the custom metadata
    /// as a base64 serialized string.
    /// This context can be used to propagate across service boundaries.
    /// </summary>
    /// <param name="customMetadata">The custom metadata to include in the context.</param>
    /// <returns>The current Sagaway context with the custom metadata as a base64 serialized string.</returns>
    string GetContextWithMetadata(string customMetadata = "");

    /// <summary>
    /// Gets the headers from the provided Sagaway context.
    /// If the context is null, the current Sagaway call context is used.
    /// </summary>
    /// <param name="context">The Sagaway context.</param>
    /// <returns>A dictionary of headers extracted from the Sagaway context.</returns>
    Dictionary<string, string> GetHeaders(SagawayContext? context);

    /// <summary>
    /// Applies a given Sagaway context to the current execution.
    /// This method is used to restore the context in downstream services.
    /// </summary>
    /// <param name="sagaContext">The serialized context string to be applied.</param>
    void ApplyContext(string sagaContext);

    /// <summary>
    /// Gets the Sagaway context from the provided headers.
    /// </summary>
    /// <param name="headers">The headers containing the Sagaway context information.</param>
    /// <returns>The Sagaway context extracted from the headers.</returns>
    SagawayContext GetSagawayContextFromHeaders(Dictionary<string, string?> headers);

    /// <summary>
    /// A helper method to convert a base64 json string to a given type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="base64String">The base64 of the json of the type</param>
    /// <returns>An instance</returns>
    T ConvertFromBase64<T>(string base64String);

    /// <summary>
    /// A helper method to convert a given instance to a base64 json string.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="instance">The source instance</param>
    /// <returns></returns>
    string ConvertToBase64<T>(T instance);
}

