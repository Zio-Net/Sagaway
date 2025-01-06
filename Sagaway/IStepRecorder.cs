namespace Sagaway;

/// <summary>
/// Record the steps of a saga, for logging and debugging purposes
/// </summary>
public interface IStepRecorder
{
    /// <summary>
    /// Load the saga log
    /// </summary>
    /// <param name="sagaId">The related saga</param>
    /// <returns>Task for an async operation</returns>
    Task LoadSagaLogAsync(string sagaId)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Store the saga log
    /// </summary>
    /// <param name="sagaId">The related saga</param>
    /// <returns>Task for an async operation</returns>
    Task SaveSagaLogAsync(string sagaId)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Record a step in the saga
    /// </summary>
    /// <param name="sagaId">The identity of the saga for the related log step</param>
    /// <param name="step">The step</param>
    /// <returns>Task for async operation</returns>
    Task RecordStepAsync(string sagaId, string step)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get the saga log
    /// </summary>
    /// <returns>The entire context of the log</returns>
    Task<string> GetSagaLogAsync()
    {
        return Task.FromResult(string.Empty);
    }
}