using Sagaway.Routing.Tracking;

namespace Sagaway.Routing.TestServiceB;

public interface ITestServiceB
{
    Task InvokeAsync(CallChainInfo request);
    Task OnResultAsync(CallChainInfo result);
}