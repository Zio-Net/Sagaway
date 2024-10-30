using Sagaway.Telemetry;
using System.Text.Json.Nodes;

namespace Sagaway.Tests
{
    class SagaTestHost : ISagaSupport
    {
        private readonly Dictionary<string, Timer> _reminders = new();
        private readonly Dictionary<string, JsonObject> _state = new();
        private readonly Func<string, Task> _reminderCallback;
        private readonly ReentrantAsyncLock _lock = new();

        // ReSharper disable once ConvertToPrimaryConstructor
        public SagaTestHost(Func<string, Task> reminderCallback)
        {
            _reminderCallback = reminderCallback;
        }

        public async Task CancelReminderAsync(string reminderName)
        {
            if (_reminders.TryGetValue(reminderName, out Timer? timer))
            {
                await timer.DisposeAsync();
                _reminders.Remove(reminderName);
            }
        }

        public ILockWrapper CreateLock()
        {
            return _lock;
        }

        public async Task<JsonObject?> LoadSagaAsync(string sagaId)
        {
            _state.TryGetValue(sagaId, out JsonObject? state);
            return await Task.FromResult(state);
        }

        public async Task SaveSagaStateAsync(string sagaId, JsonObject state)
        {
            _state[sagaId] = state;
            await Task.CompletedTask;
        }

        public async Task SetReminderAsync(string reminderName, TimeSpan dueTime)
        {
            // Dispose the existing timer if it exists
            if (_reminders.TryGetValue(reminderName, out Timer? existingTimer))
            {
                await existingTimer.DisposeAsync();
                _reminders.Remove(reminderName);
            }

            // ReSharper disable once AsyncVoidLambda
            var timer = new Timer(async state =>
            {
                // Use the semaphore to ensure that only one reminder callback is executed at a time
                await _lock.LockAsync(async () =>
                {

                    var name = (string)state!;
                    //check if the reminder is still valid
                    if (_reminders.ContainsKey(name))
                    {
                        await _reminderCallback(name);
                    }
                });

            }, reminderName, dueTime, dueTime);

            _reminders[reminderName] = timer;
            await Task.CompletedTask;
        }

        public ITelemetryAdapter TelemetryAdapter { get; } = new TestTelemetryAdapter();
    }
}
