namespace Sagaway;

/// <summary>
/// A pre-built exponential backoff function
/// </summary>
public static class ExponentialBackoff
{
    // ReSharper disable once StaticMemberInGenericType
    private static readonly Random Random = new();

    /// <summary>
    /// Create an exponential backoff function in seconds with a max of 32 seconds and a jitter factor of 0.1 by default
    /// </summary>
    /// <param name="initDelay">The initial delay in seconds</param>
    /// <param name="maxSeconds">The maximum delay</param>
    /// <param name="jitterFactor">The delay variance in seconds</param>
    /// <returns>An exponential backoff delay calculation function in seconds</returns>
    public static Func<int, TimeSpan> InSeconds(int initDelay = 10, int maxSeconds = 32, double jitterFactor = 0.1)
    {
        var maxDelay = TimeSpan.FromSeconds(maxSeconds);
        var initialDelay = TimeSpan.FromSeconds(initDelay);
        return GetExponentialBackoffFunc(maxDelay, initialDelay, TimeSpan.FromSeconds, jitterFactor);
    }

    /// <summary>
    /// Create an exponential backoff function in minutes with a max of 8 minutes and a jitter factor of 0.1 by default
    /// </summary>
    /// <param name="initDelay">The initial delay in minutes</param>
    /// <param name="maxMinutes">The maximum delay in minutes</param>
    /// <param name="jitterFactor">The delay variance</param>
    /// <returns>An exponential backoff delay calculation function in minutes</returns>
    public static Func<int, TimeSpan> InMinutes(int initDelay = 1, int maxMinutes = 8, double jitterFactor = 0.1)
    {
        var maxDelay = TimeSpan.FromMinutes(maxMinutes);
        var initialDelay = TimeSpan.FromMinutes(initDelay);
        return GetExponentialBackoffFunc(maxDelay, initialDelay, TimeSpan.FromMinutes, jitterFactor);
    }

    /// <summary>
    /// Create a general exponential backoff function
    /// </summary>
    /// <param name="maxDelay">The maximum delay of the function</param>
    /// <param name="initialDelay">The start delay</param>
    /// <param name="unitOfTime">The base unit of time for the exponential delay calculation</param>
    /// <param name="jitterFactor">The time variance</param>
    /// <returns>An exponential backoff delay calculation function</returns>
    private static Func<int, TimeSpan> GetExponentialBackoffFunc(TimeSpan maxDelay, TimeSpan initialDelay,
        Func<double, TimeSpan> unitOfTime, double jitterFactor)
    {
        return retryCount =>
        {
            var exponentialDelay = Math.Pow(2, retryCount) + initialDelay.TotalSeconds / unitOfTime(1).TotalSeconds;
            var jitter =
                (Random.NextDouble() * 2 - 1) * jitterFactor; // Random between -jitterFactor and +jitterFactor
            var calculatedDelay = Math.Min(maxDelay.TotalSeconds, exponentialDelay * (1 + jitter));
            var finalDelaySeconds = Math.Max(initialDelay.TotalSeconds, calculatedDelay);
            return unitOfTime(finalDelaySeconds);
        };
    }
}
