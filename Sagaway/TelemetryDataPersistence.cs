using System.Collections.Concurrent;
using Sagaway.Telemetry;

namespace Sagaway;

public partial class Saga<TEOperations>
{
    #region Persistent State

    private readonly ConcurrentDictionary<string, string> _telemetryStateStore = new ();

    #endregion //Persistent State

    public class TelemetryDataPersistence(Saga<TEOperations> saga) : ITelemetryDataPersistence
    {
        public Task StoreDataAsync(string key, string value)
        {
            saga._telemetryStateStore[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> RetrieveDataAsync(string key)
        {
            saga._telemetryStateStore.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task DeleteDataAsync(string key)
        {
            saga._telemetryStateStore.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
