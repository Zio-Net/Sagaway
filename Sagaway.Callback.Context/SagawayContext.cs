using System.Text.Json;
using System.Text;

namespace Sagaway.Callback.Context;

/*
 * Explanation of Each Field:
CallerId:

Purpose: Identifies the entity making the call. For actor-based services, this would be the ActorId. For non-actor services, this can remain null.
Usage: This is helpful in routing callbacks to the correct caller.
CallerType:

Purpose: Represents the type of the caller. For actors, this is the ActorType (e.g., OrderActor). For non-actor services, this could be the interface name (e.g., IOrderService).
Usage: This ensures that you can route the callback or the next call to the correct service or actor type.
CallbackBindingName:

Purpose: This is the binding name used by Dapr to determine which binding to invoke when calling back. It's specific to Dapr binding services.
Usage: For systems using Dapr, this helps direct the callback to the appropriate binding.
CallbackMethodName:

Purpose: The method name to be invoked on callback. This ensures that when the service receives the callback, it knows which method to call.
Usage: This allows precise control over which function gets invoked when the callback occurs.
MessageDispatchTime:

Purpose: The timestamp of when the message was sent, usually in ISO 8601 format (e.g., "2023-10-03T12:34:56.789Z") or a unix time.
Usage: Useful for tracing and debugging to determine how long the message took to travel between services, helping track the flow of messages in complex systems.
Metadata:

Purpose: Holds custom metadata for the call, typically serialized in JSON or another format, allowing for extra context or information to be passed along with the call.
Usage: Provides flexibility, as services can add any relevant information to be passed along the call chain or callback.
 */

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedMember.Global

/// <summary>
/// Represents the context of a Sagaway call, encapsulating various information
/// needed for routing, callback, and metadata propagation.
/// </summary>
public record SagawayContext
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="SagawayContext"/> class with the provided information.
    /// </summary>
    /// <param name="callerId">The identifier of the caller (ActorId for actors or service id for non-actor services).</param>
    /// <param name="callerType">The type of the caller (ActorType for actors or interface name for non-actor services).</param>
    /// <param name="callbackBindingName">The binding name used by Dapr to route the callback.</param>
    /// <param name="callbackMethodName">The method name to be invoked on callback.</param>
    /// <param name="messageDispatchTime">The time the message was dispatched, in ISO 8601 format.</param>
    /// <param name="metadata">Custom metadata for the call, serialized in Base64.</param>
    private SagawayContext(string? callerId = null, string? callerType = null,
        string? callbackBindingName = null, string? callbackMethodName = null,
        string? messageDispatchTime = null, string? metadata = null)
    {
        CallerId = callerId;
        CallerType = callerType;
        CallbackBindingName = callbackBindingName;
        CallbackMethodName = callbackMethodName;
        MessageDispatchTime = messageDispatchTime;
        _metadata = metadata;
    }

    /// <summary>
    /// Gets the identifier of the caller (ActorId for actors or service id for non-actor services).
    /// </summary>
    public string? CallerId { get; }

    /// <summary>
    /// Gets the type of the caller (ActorType for actors or interface name for non-actor services).
    /// </summary>
    public string? CallerType { get; }

    /// <summary>
    /// Gets the binding name used by Dapr to route the callback.
    /// </summary>
    public string? CallbackBindingName { get; }

    /// <summary>
    /// Gets the method name to be invoked on callback.
    /// </summary>
    public string? CallbackMethodName { get; }

    /// <summary>
    /// Gets the time the message was dispatched, in ISO 8601 format.
    /// </summary>
    public string? MessageDispatchTime { get; }

    private string? _metadata;

    /// <summary>
    /// Creates a new <see cref="SagawayContext"/> with metadata.
    /// </summary>
    /// <typeparam name="TMetadata">The type of the metadata to be attached to the context.</typeparam>
    /// <param name="callerId">The identifier of the caller (ActorId or service id).</param>
    /// <param name="callerType">The type of the caller (ActorType or interface name).</param>
    /// <param name="callbackBindingName">The binding name for the callback.</param>
    /// <param name="callbackMethodName">The method name to be invoked on callback.</param>
    /// <param name="metadata">The metadata to attach, serialized and stored in Base64.</param>
    /// <returns>A new instance of <see cref="SagawayContext"/>.</returns>
    public static SagawayContext Create<TMetadata>(string callerId, string callerType, string callbackBindingName, string callbackMethodName, TMetadata metadata)
    {
        var context = new SagawayContext(
            callerId,
            callerType,
            callbackBindingName,
            callbackMethodName,
            DateTime.UtcNow.ToString("o")
        );

        if (metadata != null)
            context.SetMetadata(metadata);

        return context;
    }

    /// <summary>
    /// Creates a new <see cref="SagawayContext"/> without metadata.
    /// </summary>
    /// <param name="callerId">The identifier of the caller (ActorId or service id).</param>
    /// <param name="callerType">The type of the caller (ActorType or interface name).</param>
    /// <param name="callbackBindingName">The binding name for the callback.</param>
    /// <param name="callbackMethodName">The method name to be invoked on callback.</param>
    /// <returns>A new instance of <see cref="SagawayContext"/>.</returns>
    public static SagawayContext Create(string callerId, string callerType, string callbackBindingName, string callbackMethodName)
    {
        return new SagawayContext(
            callerId,
            callerType,
            callbackBindingName,
            callbackMethodName,
            DateTime.UtcNow.ToString("o")
        );
    }

    /// <summary>
    /// Sets the metadata for the current context, serializing it to Base64.
    /// </summary>
    /// <typeparam name="T">The type of the metadata.</typeparam>
    /// <param name="metadata">The metadata to serialize and attach to the context.</param>
    // ReSharper disable once MemberCanBePrivate.Global
    public void SetMetadata<T>(T metadata)
    {
        if (metadata == null)
            return;

        var json = JsonSerializer.Serialize(metadata, _jsonSerializerOptions);
        _metadata = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Retrieves the metadata attached to the current context, deserializing it from Base64.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the metadata into.</typeparam>
    /// <returns>The deserialized metadata, or the default value if no metadata is present.</returns>
    public T GetMetadata<T>()
    {
        if (string.IsNullOrEmpty(_metadata))
            return default!;

        var decodedBase64 = Encoding.UTF8.GetString(Convert.FromBase64String(_metadata));

        return string.IsNullOrEmpty(decodedBase64)
            ? default!
            : JsonSerializer.Deserialize<T>(decodedBase64, _jsonSerializerOptions)!;
    }
}
