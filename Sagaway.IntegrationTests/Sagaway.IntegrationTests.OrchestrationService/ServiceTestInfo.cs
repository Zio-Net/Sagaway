namespace Sagaway.IntegrationTests.OrchestrationService;

// ReSharper disable once ClassNeverInstantiated.Global
public record ServiceTestInfo
{
    //unique id for the call
    public string CallId { get; init; } = Guid.NewGuid().ToString();

    //array of bool to define which call should fail
    public bool[]? FailureOnCall { get; init; }

    //array of int to define the delay on each call
    public int[]? DelayOnCallInSeconds { get; init; }

    //array of bool to define if the call should return a success result
    public bool[]? SuccessOnCall { get; init; }

    //array of bool to define if the call should return a result in the callback
    public bool[]? ShouldReturnCallbackResultOnCall { get; init; }

    //should revert the success result
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public bool IsReverting { get; set; }
}