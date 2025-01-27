using Sagaway.Callback.Router;

namespace Sagaway.IntegrationTests.TestSubSagaCommunicationService;

public interface ISubSagaActor : ISagawayActor
{
    Task AddAsync(int a, int b, TimeSpan delay);
    Task DoneAsync();
}