namespace Sagaway.IntegrationTests.TestService;

// ReSharper disable once ClassNeverInstantiated.Global
public record ServiceTestInfo
{
    //unique id for the call
    public string CallId { get; init; } = Guid.NewGuid().ToString();

    //define the delay on each call
    public int DelayOnCallInSeconds { get; init; } = 0;

    //define if the call should return succeeded
    public bool ShouldSucceed { get; init; } = true;

    //define if the call should return a result in the callback
    public bool ShouldReturnCallbackResult { get; init; } = true;

    //should revert the success result
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public bool IsReverting { get; set; }
}