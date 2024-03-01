using System.Text.Json.Nodes;

namespace Sagaway.Tests;

class SagaSupportOperations : ISagaSupport
{
    private readonly Dictionary<string, Timer> _reminders = new();
    private readonly Dictionary<string, JsonObject> _state = new();
    private readonly Func<string, Task> _reminderCallback;


    // ReSharper disable once ConvertToPrimaryConstructor
    public SagaSupportOperations(Func<string, Task> reminderCallback)
    {
        _reminderCallback = reminderCallback;
    }
    
    public async Task CancelReminderAsync(string reminderName)
    {
        await _reminders[reminderName].DisposeAsync();
        _reminders.Remove(reminderName);
    }

    public ILockWrapper CreateLock()
    {
        return new ReentrantAsyncLock();
    }

    public async Task<JsonObject?> LoadSagaAsync(string sagaId)
    {
        _state.TryGetValue(sagaId, out JsonObject? state);
        return await Task.FromResult(state ?? new JsonObject());
    }

    public async Task SaveSagaStateAsync(string sagaId, JsonObject state)
    {
        _state[sagaId] = state;
        await Task.CompletedTask;
    }

    public async Task SetReminderAsync(string reminderName, TimeSpan dueTime)
    {
        var timer = new Timer( state => _reminderCallback((string)state!), reminderName, (int)dueTime.TotalMilliseconds, (int)dueTime.TotalMilliseconds);
        _reminders[reminderName] = timer;
        await Task.CompletedTask;
    }
}
