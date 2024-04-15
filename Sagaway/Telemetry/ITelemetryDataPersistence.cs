namespace Sagaway.Telemetry;

/// <summary>
/// Defines a contract for persisting telemetry data asynchronously.
/// This interface allows telemetry components to store, retrieve, and delete
/// persistence data required for maintaining state across telemetry operations.
/// </summary>
public interface ITelemetryDataPersistence
{
    /// <summary>
    /// Asynchronously stores data with a specified key.
    /// </summary>
    /// <param name="key">The key under which the data is stored. Must be unique across the telemetry context.</param>
    /// <param name="value">The data value to store.</param>
    /// <returns>A task that represents the asynchronous store operation.</returns>
    Task StoreDataAsync(string key, string value);

    /// <summary>
    /// Asynchronously retrieves data associated with the specified key.
    /// </summary>
    /// <param name="key">The key for which data is to be retrieved.</param>
    /// <returns>
    /// A task that represents the asynchronous retrieve operation.
    /// The task result contains the data value associated with the specified key, or null if the key does not exist.
    /// </returns>
    Task<string?> RetrieveDataAsync(string key);

    /// <summary>
    /// Asynchronously deletes data associated with the specified key.
    /// </summary>
    /// <param name="key">The key for which data is to be deleted.</param>
    /// <returns>A task that represents the asynchronous delete operation.</returns>
    Task DeleteDataAsync(string key);
}