using Dapr.Actors;

namespace Sagaway.IntegrationTests.OrchestrationService.Actors;

public interface ITestActor : IActor
{
    Task RunTestAsync(TestInfo? testInfo);
    Task ResetSagaAsync();
}