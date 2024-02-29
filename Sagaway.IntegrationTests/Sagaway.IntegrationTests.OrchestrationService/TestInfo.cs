namespace Sagaway.IntegrationTests.OrchestrationService;

public record TestInfo
{
    public string TestName { get; init; } = "";
    public Guid Id { get; init; } = Guid.Empty;
    public ServiceTestInfo? ServiceACall { get; set; } = new();
    public ServiceTestInfo? ServiceARevert { get; set; } = new();
    public ServiceTestInfo? ServiceBCall { get; set; } = new();
    public ServiceTestInfo? ServiceBRevert { get; set; } = new();
}