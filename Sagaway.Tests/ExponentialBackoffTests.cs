namespace Sagaway.Tests;

public class ExponentialBackoffTests
{
    [Fact]
    public void InMinutes_Backoff_Delays_Are_Within_Expected_Range()
    {
        // Arrange
        int initialDelay = 1;
        int maxMinutes = 8;
        double jitterFactor = 0.1;
        var backoffFunc = ExponentialBackoff.InMinutes(initialDelay, maxMinutes, jitterFactor);

        int maxRetryCount = 5;

        for (int retryCount = 0; retryCount <= maxRetryCount; retryCount++)
        {
            // Act
            TimeSpan delay = backoffFunc(retryCount);

            // Calculate expected delay units
            double initialDelayUnits = initialDelay;
            double maxDelayUnits = maxMinutes;
            double exponentialDelayUnits = initialDelayUnits + Math.Pow(2, retryCount);

            // Calculate possible delay range considering jitter
            double minPossibleDelayUnits = exponentialDelayUnits * (1 - jitterFactor);
            double maxPossibleDelayUnits = exponentialDelayUnits * (1 + jitterFactor);

            // Ensure delays are within [initialDelayUnits, maxDelayUnits]
            minPossibleDelayUnits = Math.Max(initialDelayUnits, minPossibleDelayUnits);
            maxPossibleDelayUnits = Math.Min(maxDelayUnits, maxPossibleDelayUnits);

            // Ensure minPossibleDelayUnits <= maxPossibleDelayUnits
            if (minPossibleDelayUnits > maxPossibleDelayUnits)
            {
                minPossibleDelayUnits = maxPossibleDelayUnits;
            }

            // Convert delay units to TimeSpan
            TimeSpan minExpectedDelay = TimeSpan.FromMinutes(minPossibleDelayUnits);
            TimeSpan maxExpectedDelay = TimeSpan.FromMinutes(maxPossibleDelayUnits);

            // Assert
            Assert.InRange(delay.TotalMinutes, minExpectedDelay.TotalMinutes, maxExpectedDelay.TotalMinutes);
        }
    }

    [Fact]
    public void InSeconds_Backoff_Delays_Are_Within_Expected_Range()
    {
        // Arrange
        int initialDelay = 10;
        int maxSeconds = 32;
        double jitterFactor = 0.1;
        var backoffFunc = ExponentialBackoff.InSeconds(initialDelay, maxSeconds, jitterFactor);

        int maxRetryCount = 5;

        for (int retryCount = 0; retryCount <= maxRetryCount; retryCount++)
        {
            // Act
            TimeSpan delay = backoffFunc(retryCount);

            // Calculate expected delay units
            double initialDelayUnits = initialDelay;
            double maxDelayUnits = maxSeconds;
            double exponentialDelayUnits = initialDelayUnits + Math.Pow(2, retryCount);

            // Calculate possible delay range considering jitter
            double minPossibleDelayUnits = exponentialDelayUnits * (1 - jitterFactor);
            double maxPossibleDelayUnits = exponentialDelayUnits * (1 + jitterFactor);

            // Ensure delays are within [initialDelayUnits, maxDelayUnits]
            minPossibleDelayUnits = Math.Max(initialDelayUnits, minPossibleDelayUnits);
            maxPossibleDelayUnits = Math.Min(maxDelayUnits, maxPossibleDelayUnits);

            // Ensure minPossibleDelayUnits <= maxPossibleDelayUnits
            if (minPossibleDelayUnits > maxPossibleDelayUnits)
            {
                minPossibleDelayUnits = maxPossibleDelayUnits;
            }

            // Convert delay units to TimeSpan
            TimeSpan minExpectedDelay = TimeSpan.FromSeconds(minPossibleDelayUnits);
            TimeSpan maxExpectedDelay = TimeSpan.FromSeconds(maxPossibleDelayUnits);

            // Assert
            Assert.InRange(delay.TotalSeconds, minExpectedDelay.TotalSeconds, maxExpectedDelay.TotalSeconds);
        }
    }
}

