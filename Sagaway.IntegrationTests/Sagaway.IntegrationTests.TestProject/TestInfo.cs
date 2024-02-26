namespace Sagaway.IntegrationTests.TestProject;

public record TestInfo
{
    public string TestName { get; init; } = "";
    public Guid Id { get; init; } = Guid.Empty;
    public ServiceTestInfo? ServiceACall { get; init; }
    public ServiceTestInfo? ServiceARevert { get; init; }
    public ServiceTestInfo? ServiceBCall { get; init; }
    public ServiceTestInfo? ServiceBRevert { get; init; }
}