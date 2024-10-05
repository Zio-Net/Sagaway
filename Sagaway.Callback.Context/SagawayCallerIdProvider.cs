using System.Reflection;
using System.Security.Cryptography;

namespace Sagaway.Callback.Context;

/// <summary>
/// Provides the caller ID for Sagaway services.
/// </summary>
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class SagawayCallerIdProvider : ISagawayCallerIdProvider
{
    private string? _uniqueServiceId;

    /// <summary>
    /// Gets or sets the caller ID.
    /// </summary>
    /// <remarks>
    /// We use this property to distinguish each service type from each other.
    /// Since multiple instances of the service can accept the call all of them need to share the same callerId we must find a unique id which is shared among the service instances.
    /// The default implementation takes the service host assembly name and version as the callerId.
    /// The Sagaway routing framework uses this caller id to check if the current context belongs to the current service
    /// and to ensure that it removes this context layer on calling back.
    /// If you have multiple services that shares the same assembly name and version, implement your own CallerId and use a custom identifier for the callerId service type.
    /// Register your custom implementation of <see cref="ISagawayCallerIdProvider"/> in the DI container.
    /// </remarks>
    public virtual string CallerId
    {
        get
        {
            if (string.IsNullOrEmpty(_uniqueServiceId))
            {
                _uniqueServiceId = GenerateUniqueServiceId();
            }

            return _uniqueServiceId;
        }
        set => _uniqueServiceId = value;
    }

    /// <summary>
    /// Generates a unique service ID based on the assembly name and version.
    /// </summary>
    /// <returns>The unique service ID.</returns>
    private string GenerateUniqueServiceId()
    {
        var assembly = Assembly.GetEntryAssembly();

        if (assembly == null)
        {
            throw new InvalidOperationException("Unable to get entry assembly.");
        }

        // Use the assembly name as a basis for the unique identifier
        string assemblyName = assembly.GetName().Name ?? "UnknownService";

        // Get the assembly version
        string assemblyVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        // Combine assembly name and version
        string combinedString = $"{assemblyName}_{assemblyVersion}";

        // Generate a hash of the combined string
        using SHA256 sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combinedString));
        return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);
    }
}
