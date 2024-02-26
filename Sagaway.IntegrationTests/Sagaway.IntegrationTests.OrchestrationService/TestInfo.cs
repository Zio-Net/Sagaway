namespace Sagaway.IntegrationTests.OrchestrationService;

public record TestInfo
{
    public string TestName { get; init; } = "";
    public Guid Id { get; init; } = Guid.Empty;
    public ServiceTestInfo? ServiceA { get; init; }
    public ServiceTestInfo? ServiceB { get; init; }
}