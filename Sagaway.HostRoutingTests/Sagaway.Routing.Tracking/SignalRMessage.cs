namespace Sagaway.Routing.Tracking;

public record SignalRMessage
{
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public string? UserId { get; set; }
    public string? GroupName { get; set; }
    public string? Target { get; set; }
    public Argument?[]? Arguments { get; set; }
}