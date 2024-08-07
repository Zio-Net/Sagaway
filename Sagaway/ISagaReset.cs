namespace Sagaway;

public interface ISagaReset
{
    /// <summary>
    /// Reset the saga state to allow re-execution
    /// </summary>
    /// <returns>Async operation</returns>
    Task ResetSagaAsync();
}