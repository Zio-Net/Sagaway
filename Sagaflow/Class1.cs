using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;

namespace Sagaflow;

public record StepExecutionOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(5);
    public double BackoffCoefficient { get; set; } = 2.0;
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromSeconds(60);
}

public class WorkflowAsyncStep<TInput, TOutput, TActivity> : Workflow<TInput, TOutput> 
    where TActivity : WorkflowActivity<TInput, TOutput>, new()
    where TOutput : new()
{
    private readonly StepExecutionOptions _stepExecutionOptions;
    private readonly ILogger? _logger;
    private readonly string _parentWorkflowInstanceId;
    private readonly string _stepInstanceName;
    private readonly Func<TOutput, bool> _successCriteria;
    private readonly DaprClient _daprClient;

    // ReSharper disable once ConvertToPrimaryConstructor
    public WorkflowAsyncStep(
        DaprClient daprClient,
        string parentWorkflowInstanceId,
        string stepInstanceName, //this name is used as the eventName in the daprClient.RaiseWorkflowEventAsync
        Func<TOutput, bool>? successCriteria,
        StepExecutionOptions? activityExecutionOptions = null,
        ILogger? logger = null)
    {
        _daprClient = daprClient;
        _parentWorkflowInstanceId = parentWorkflowInstanceId;
        _stepInstanceName = stepInstanceName;
        _successCriteria = successCriteria ?? (_ => true);
        _stepExecutionOptions = activityExecutionOptions ?? new();
        _logger = logger;
    }

    public override async Task<TOutput> RunAsync(WorkflowContext context, TInput input)
    {
        var retryCount = 0;
        TimeSpan currentRetryInterval = _stepExecutionOptions.RetryInterval;

        while (true)
        {
            try
            {
                // Call the activity and wait for an event indicating completion
                await context.CallActivityAsync<TOutput>(nameof(TActivity), input);
                var result = await context.WaitForExternalEventAsync<TOutput>(_stepInstanceName, currentRetryInterval);

                // Check if the result meets the success criteria
                if (_successCriteria(result))
                {
                    // Success: raise an event and return the result
#pragma warning disable CS0618 // Type or member is obsolete due to alpha version of the API
                    await _daprClient.RaiseWorkflowEventAsync(_parentWorkflowInstanceId, "dapr", _stepInstanceName, result);
#pragma warning restore CS0618 // Type or member is obsolete
                    return result;
                }
                throw new Exception($"Activity {nameof(TActivity)} failed to meet success criteria");
            }
            catch (Exception e)
            {
                if (retryCount >= _stepExecutionOptions.MaxRetries)
                {
                    // Max retries exceeded: log error and return a new instance of TOutput
                    _logger?.LogError(e, "Activity {activityName} failed after {RetryCount} retries", nameof(TActivity), retryCount);
                    return new TOutput(); // Consider enriching TOutput to indicate failure
                }
                _logger?.LogWarning(e, "Activity {activityName} failed, retrying after {RetryInterval}", nameof(TActivity), currentRetryInterval);

                // Calculate the next retry interval with exponential backoff, respecting the max retry interval
                currentRetryInterval = TimeSpan.FromSeconds(Math.Min(
                    _stepExecutionOptions.MaxRetryInterval.TotalSeconds,
                    currentRetryInterval.TotalSeconds * _stepExecutionOptions.BackoffCoefficient));

                // Log the next retry attempt
                DateTime wakeUpTime = context.CurrentUtcDateTime.Add(currentRetryInterval);
                _logger?.LogInformation("Activity {activityName} retrying at {WakeUpTime}", nameof(TActivity), wakeUpTime);

                // Create a timer to delay the next retry
                await context.CreateTimer(wakeUpTime, CancellationToken.None);
                retryCount++;
            }
        }
    }
}

