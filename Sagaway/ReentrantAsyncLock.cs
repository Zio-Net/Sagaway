namespace Sagaway;

/// <summary>
/// Implement a real lock wrapper
/// </summary>
public class ReentrantAsyncLock : ILockWrapper
{
    private readonly AsyncLocal<ReentrantLockState> _reentrantLockState = new AsyncLocal<ReentrantLockState>();
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Lock the action
    /// </summary>
    /// <param name="action">The action to be executed by a sin</param>
    /// <returns></returns>
    public async Task LockAsync(Func<Task> action)
    {
        _reentrantLockState.Value ??= new ReentrantLockState { LockCount = 0 };

        if (_reentrantLockState.Value.LockCount == 0)
        {
            await _semaphore.WaitAsync();
        }

        _reentrantLockState.Value.LockCount++;

        try
        {
            await action();
        }
        finally
        {
            _reentrantLockState.Value.LockCount--;
            if (_reentrantLockState.Value.LockCount == 0)
            {
                _semaphore.Release();
            }
        }
    }

    private class ReentrantLockState
    {
        public int LockCount { get; set; }
    }
}

