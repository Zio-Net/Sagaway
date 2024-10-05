using Microsoft.Extensions.Logging;
using Sagaway.Callback.Context;

namespace Sagaway.Callback.Propagator;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global


/// <summary>
/// Default implementation of the <see cref="ISagawayCallbackMetadataProvider"/> interface.
/// Provides the necessary callback metadata for Dapr's `InvokeBindingAsync` to route callbacks 
/// correctly back to the caller service.
/// </summary>
public class SagawayCallbackMetadataProvider : ISagawayCallbackMetadataProvider, ISagawayContextInformationProvider
{
    private readonly ILogger<SagawayCallbackMetadataProvider> _logger;
    private readonly ISagawayContextManager _sagawayContextManager;
    private readonly ISagawayCallerIdProvider _sagawayCallerIdProvider;
    private readonly IServiceProvider _serviceProvider;

    // ReSharper disable once ConvertToPrimaryConstructor
    /// <summary>
    /// Initializes a new instance of the <see cref="SagawayCallbackMetadataProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger for recording information and errors.</param>
    /// <param name="sagawayContextManager">The context manager for managing the Sagaway context.</param>
    /// <param name="sagawayCallerIdProvider">Provides the caller ID for the current service.</param>
    /// <param name="serviceProvider">The service provider for checking registered services in DI.</param>
    public SagawayCallbackMetadataProvider(
        ILogger<SagawayCallbackMetadataProvider> logger,
        ISagawayContextManager sagawayContextManager,
        ISagawayCallerIdProvider sagawayCallerIdProvider,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _sagawayContextManager = sagawayContextManager;
        _sagawayCallerIdProvider = sagawayCallerIdProvider;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the callback binding name used for routing the response of the invoked Dapr binding.
    /// </summary>
    public string? CallbackBindingName => _sagawayContextManager.GetCallerContext()?.CallbackBindingName;

    /// <summary>
    /// Gets the callback metadata for Dapr to correctly route the response for a downstream call.
    /// </summary>
    /// <param name="callbackMethodName">The name of the method that should be called upon callback.</param>
    /// <param name="registeredServiceType">The type of the registered service in the DI container.</param>
    /// <param name="daprBindingName">The Dapr binding name that will be invoked.</param>
    /// <returns>A dictionary containing metadata to be added to the Dapr binding call.</returns>
    /// <exception cref="ArgumentException">Thrown if the method name, service type, or binding name are invalid.</exception>
    public IReadOnlyDictionary<string, string> GetCallbackMetadata(
        string callbackMethodName, Type registeredServiceType, string daprBindingName)
    {
        return GetCallbackMetadata<object?>(callbackMethodName, registeredServiceType, daprBindingName, null);
    }

    /// <summary>
    /// Gets the callback metadata along with custom metadata for routing the response of the invoked Dapr binding.
    /// </summary>
    /// <typeparam name="TMetadata">The type of custom metadata to be included in the callback.</typeparam>
    /// <param name="callbackMethodName">The name of the method that should be called upon callback.</param>
    /// <param name="registeredServiceType">The type of the registered service in the DI container.</param>
    /// <param name="daprBindingName">The Dapr binding name that will be invoked.</param>
    /// <param name="customMetadata">The custom metadata to be passed along with the callback.</param>
    /// <returns>A dictionary containing metadata and custom metadata to be added to the Dapr binding call.</returns>
    /// <exception cref="ArgumentException">Thrown if the method name, service type, or binding name are invalid.</exception>
    public IReadOnlyDictionary<string, string> GetCallbackMetadata<TMetadata>(
        string callbackMethodName, Type registeredServiceType, string daprBindingName, TMetadata customMetadata)
    {
        try
        {
            if (string.IsNullOrEmpty(callbackMethodName))
            {
                _logger.LogError("Callback method name is null or empty.");
                throw new ArgumentException("Callback method name cannot be null or empty.",
                    nameof(callbackMethodName));
            }

            // Check if the service type is registered
            bool isRegistered = _serviceProvider.GetService(registeredServiceType) != null;

            if (!isRegistered)
            {
                _logger.LogError("Service type {ServiceType} is not registered in the service provider.",
                    registeredServiceType.FullName);
                throw new ArgumentException(
                    $"Service type {registeredServiceType.FullName} is not registered in the service provider.",
                    nameof(registeredServiceType));
            }

            if (string.IsNullOrEmpty(daprBindingName))
            {
                _logger.LogError("Dapr binding name is null or empty. Caller can't callback to the current service with out a binding");
                throw new ArgumentException("Dapr binding name cannot be null or empty.",
                    nameof(daprBindingName));
            }

            SagawayContext callerContext = SagawayContext.Create(
                _sagawayCallerIdProvider.CallerId,
                registeredServiceType.FullName!,
                daprBindingName,
                callbackMethodName,
                customMetadata
            );

            var downStreamCallContext = _sagawayContextManager.GetDownStreamCallContext(callerContext);

            return downStreamCallContext;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting callback metadata.");
            throw;
        }
    }

    /// <summary>
    /// Gets the message dispatch time from the current context.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if no dispatch time is found in the context.</exception>
    public DateTimeOffset MessageDispatchTime => DateTimeOffset.Parse(_sagawayContextManager.GetCallerContext()?.MessageDispatchTime ?? throw new InvalidOperationException("No message dispatch time exist"));


    /// <summary>
    /// Retrieves custom metadata from the current context.
    /// </summary>
    /// <typeparam name="TMetadata">The type of the custom metadata.</typeparam>
    /// <returns>The custom metadata, or the default value for the specified type if not present.</returns>
    public TMetadata GetCustomMetadata<TMetadata>()
    {
        var context = _sagawayContextManager.GetCallerContext();
        if (context is null)
        {
            _logger.LogWarning("No caller context found. Returning default value for metadata type {MetadataType}", typeof(TMetadata).FullName);
            return default!;
        }
        return context.GetMetadata<TMetadata>();
    }
}