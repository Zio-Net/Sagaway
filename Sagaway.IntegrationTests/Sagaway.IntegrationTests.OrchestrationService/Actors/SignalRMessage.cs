namespace Sagaway.IntegrationTests.OrchestrationService.Actors;

public record SignalRMessage
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public string? Target { get; set; }
    public Argument?[]? Arguments { get; set; }
}