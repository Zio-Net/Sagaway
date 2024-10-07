using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sagaway.Callback.Context;

/// <summary>
/// Manages the Sagaway context stack, which holds context information for downstream
/// and upstream service calls. Supports adding, removing, serializing, and deserializing
/// context layers as well as preparing contexts for callbacks and downstream calls.
/// </summary>
public class SagawayContextManager : ISagawayContextManager
{
    // ImmutableStack to manage multiple context layers for nested or downstream service calls.
    private ImmutableStack<SagawayContext> _contextStack = ImmutableStack<SagawayContext>.Empty;
    private const string SagaWayContextHeader = "x-sagaway-dapr-context";
    private const string SagawayMessageDispatchTimeHeader = "x-sagaway-dapr-message-dispatch-time";
    private readonly string _callerId;
    private readonly ILogger<SagawayContextManager> _logger;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new ImmutableStackJsonConverter<SagawayContext>() }
    };

    /// <summary>
    /// Gets the key name for the Sagaway context header used in HTTP requests.
    /// </summary>
    public string SagaWayContextHeaderKeyName  => SagaWayContextHeader;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagawayContextManager"/> class.
    /// </summary>
    /// <param name="sagawayCallerIdProvider">Provides the caller ID for the current service.</param>
    /// <param name="logger">Logger instance for logging operations and messages.</param>
    public SagawayContextManager(ISagawayCallerIdProvider sagawayCallerIdProvider, ILogger<SagawayContextManager> logger)
    {
        _callerId = sagawayCallerIdProvider.CallerId;
        _logger = logger;

        _logger.LogInformation("SagawayContextManager initialized for caller: {CallerId}", _callerId);
    }
    /// <summary>
    /// Prepares the context for a downstream call by adding the new context layers to the stack.
    /// </summary>
    /// <param name="currentContext">The current context to be added to the stack.</param>
    /// <param name="optionalTargetContext">Optional target context for specific downstream calls.</param>
    /// <returns>A dictionary with headers representing the serialized context and message dispatch time.</returns>
    public IReadOnlyDictionary<string, string> GetDownStreamCallContext(SagawayContext currentContext, SagawayContext? optionalTargetContext = null)
    {
        _logger.LogInformation("Preparing downstream call context for caller: {CallerId}", currentContext.CallerId);

        var contextStackText = CloneAndAddNewContextLayers(currentContext, optionalTargetContext);

        _logger.LogDebug("Serialized downstream call context: {ContextStackText}", contextStackText);

        return new Dictionary<string, string>()
        {
            { SagaWayContextHeader, contextStackText },
            { SagawayMessageDispatchTimeHeader, currentContext.MessageDispatchTime ?? DateTime.UtcNow.ToString("o")}
        };
    }

    /// <summary>
    /// Prepares the context for an upstream callback, removing the current layer from the stack.
    /// </summary>
    /// <returns>A dictionary with headers representing the serialized context and message dispatch time.</returns>
    public IReadOnlyDictionary<string, string> GetUpStreamCallContext()
    {
        var currentContext = GetCallerContext();

        var upStreamStackText = currentContext?.CallerId == _callerId
            ? RemoveOneLayerAndClone()
            : SerializeContextStack();

        _logger.LogInformation("Preparing upstream callback context for caller: {CallerId}", _callerId);
        _logger.LogDebug("Serialized upstream call context: {UpStreamStackText}", upStreamStackText);

        return new Dictionary<string, string>()
        {
            { SagaWayContextHeader, upStreamStackText },
            { SagawayMessageDispatchTimeHeader, currentContext?.MessageDispatchTime ?? DateTime.UtcNow.ToString("o")}
        };
    }

    /// <summary>
    /// Retrieves the current active context from the top of the stack.
    /// </summary>
    /// <returns>The caller's <see cref="SagawayContext"/> or null if the stack is empty.</returns>
    public SagawayContext? GetCallerContext()
    {
        if (_contextStack.IsEmpty)
        {
            _logger.LogWarning("Context stack is empty. No caller context available.");
            return null;
        }

        var callerContext = _contextStack.Peek();
        _logger.LogInformation("Retrieved caller context: {CallerId}", callerContext.CallerId);
        return callerContext;
    }

    /// <summary>
    /// Sets the context from an incoming HTTP request by deserializing the base64-encoded context stack.
    /// </summary>
    /// <param name="base64ContextStack">The base64-encoded context stack string received in the request headers.</param>
    public void SetContextFromIncomingRequest(string base64ContextStack)
    {
        if (!string.IsNullOrWhiteSpace(base64ContextStack))
        {
            _logger.LogInformation("Setting context from incoming request...");
            DeserializeAndSetContextStack(base64ContextStack);
            _logger.LogDebug("Context stack successfully deserialized.");
        }
        else
        {
            _logger.LogWarning("No context provided in the incoming request.");
        }
    }

    /// <summary>
    /// Clones the current stack and adds new context layers for downstream calls.
    /// </summary>
    /// <param name="newContexts">The new contexts to add to the stack.</param>
    /// <returns>A base64-encoded string of the updated stack.</returns>
    private string CloneAndAddNewContextLayers(params SagawayContext? [] newContexts)
    {
        _logger.LogInformation("Cloning and adding new context layers for downstream call.");

        var clonedStack = _contextStack;

        foreach (var newContext in newContexts)
        {
            if (newContext != null)
                clonedStack = clonedStack.Push(newContext);
        }

        // Serialize the updated stack and return it as a Base64 string
        var serializedStack = SerializeContextStack(clonedStack);
        _logger.LogDebug("New context layer added and serialized: {SerializedStack}", serializedStack);

        return serializedStack;
    }

    /// <summary>
    /// Removes the top layer from the stack and returns the serialized result for upstream callbacks.
    /// </summary>
    /// <returns>A base64-encoded string of the updated stack.</returns>
    private string RemoveOneLayerAndClone()
    {
        _logger.LogInformation("Removing top context layer and cloning the stack.");

        var clonedStack = _contextStack.Pop();

        var serializedStack = SerializeContextStack(clonedStack);
        _logger.LogDebug("Context stack after removing one layer: {SerializedStack}", serializedStack);

        return serializedStack;
    }

    /// <summary>
    /// Serializes the provided stack into a base64-encoded string with gzip compression.
    /// </summary>
    /// <param name="stack">The context stack to serialize (uses current stack if null).</param>
    /// <returns>A base64-encoded string representing the compressed stack.</returns>
    private string SerializeContextStack(ImmutableStack<SagawayContext>? stack = null)
    {
        // If no stack is provided, use the current stack
        stack ??= _contextStack;

        _logger.LogDebug("Serializing context stack...");

        // Serialize the stack to JSON
        var jsonContextList = JsonSerializer.Serialize(stack, _jsonSerializerOptions);
        var uncompressedBytes = Encoding.UTF8.GetBytes(jsonContextList);

        // Compress the serialized JSON
        using var outputStream = new MemoryStream();

        using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true))
        {
            gzipStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
        }

        var compressedBytes = outputStream.ToArray();
        return Convert.ToBase64String(compressedBytes);
    }

    /// <summary>
    /// Deserializes and decompresses a base64-encoded context stack string, reconstructing the stack.
    /// </summary>
    /// <param name="base64ContextStack">The base64-encoded context stack string.</param>
    private void DeserializeAndSetContextStack(string base64ContextStack)
    {
        _logger.LogInformation("Deserializing and decompressing context stack from base64 string...");

        // Convert base64 back to compressed byte array
        var compressedBytes = Convert.FromBase64String(base64ContextStack);

        // Decompress the byte array
        using var inputStream = new MemoryStream(compressedBytes);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();
        gzipStream.CopyTo(decompressedStream);

        var decompressedBytes = decompressedStream.ToArray();
        var jsonContextStack = Encoding.UTF8.GetString(decompressedBytes);

        // Deserialize the JSON back to the ImmutableStack
        _contextStack = JsonSerializer.Deserialize<ImmutableStack<SagawayContext>>(jsonContextStack, _jsonSerializerOptions)
                        ?? throw new InvalidOperationException("Failed to deserialize the context stack.");

        _logger.LogDebug("Context stack successfully deserialized and decompressed.");
    }

}
