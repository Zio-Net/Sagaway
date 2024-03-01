namespace Sagaway.IntegrationTests.TestProject;

public record TestResult
{
    public TestInfo TestInfo { get; set; } = new();
    public bool IsSuccess { get; set; }
    public string SagaLog { get; set; } = string.Empty;
}