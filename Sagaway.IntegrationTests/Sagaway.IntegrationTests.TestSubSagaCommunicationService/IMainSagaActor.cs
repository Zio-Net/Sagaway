using Dapr.Actors;
using Sagaway.Callback.Router;

namespace Sagaway.IntegrationTests.TestSubSagaCommunicationService;

public interface IMainSagaActor : ISagawayActor
{
    Task RunTestAsync();
}