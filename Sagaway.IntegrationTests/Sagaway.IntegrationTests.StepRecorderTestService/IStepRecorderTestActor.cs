using Dapr.Actors;

namespace Sagaway.IntegrationTests.StepRecorderTestService;

public interface IStepRecorderTestActor : IActor
{
    Task RunSagaAsync();
}