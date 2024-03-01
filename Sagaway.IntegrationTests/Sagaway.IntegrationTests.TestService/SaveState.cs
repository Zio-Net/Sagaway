namespace Sagaway.IntegrationTests.TestService;

public record SaveState
{
    public string CallerId {get; init; } = string.Empty;
    public DateTime MessageDispatchTime {get; init; }
}