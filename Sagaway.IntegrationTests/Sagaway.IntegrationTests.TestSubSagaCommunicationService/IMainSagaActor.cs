using Sagaway.Hosts;
using Sagaway.Hosts.DaprActorHost;

namespace Sagaway.IntegrationTests.TestSubSagaCommunicationService;

public interface IMainSagaActor : ISagawayActor
{
    Task RunTestAsync();
    Task<TestResult> GetTestResultAsync();
}