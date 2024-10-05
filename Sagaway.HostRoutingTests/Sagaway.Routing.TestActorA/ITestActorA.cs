using Dapr.Actors;
using Sagaway.Routing.Tracking;

namespace Sagaway.Routing.TestActorA;

public interface ITestActorA : IActor
{
    Task InvokeAsync(CallChainInfo request);
}