namespace Sagaway;

/// <summary>
/// A pseudo lock that does not actually lock anything.
/// </summary>
public class NonLockAsync : ILockWrapper
{
    /// <summary>
    /// Pseudo lock that does not actually lock anything.
    /// </summary>
    /// <param name="action">The locked segment</param>
    /// <returns>Nothing</returns>
    public Task LockAsync(Func<Task> action)
    {
        return action();
    }
}