namespace Sagaway;

/// <summary>
/// Provide a way to lock the saga for a single thread operations
/// on a multithreading environment
/// </summary>
public interface ILockWrapper
{
    /// <summary>
    /// Provide a way to lock the saga for a single thread operations
    /// </summary>
    /// <param name="action">The guard code</param>
    /// <returns></returns>
    Task LockAsync(Func<Task> action);
}